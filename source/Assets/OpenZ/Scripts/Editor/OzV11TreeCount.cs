using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted check of the trees that can be felled (DV88). Play mode on level 16 in a game against the CPU, best right after the
    /// level started, at game speed 1x; it takes about a minute and leaves its staged units in the match (start the level again
    /// afterwards). A second start while it runs does nothing.
    ///  1. start: every tree of the list is in the rules, closes its tile as a barrier that can be destroyed, and has its drawn tree
    ///     and its run in a far mesh;
    ///  2. the order: a right-click on a tree gives a unit with explosives an attack order (the game's own order table), the rules
    ///     accept it, a unit without explosives cannot attack a tree;
    ///  3. half its points: a tree leans (the damaged stage) with a creak in the sound log;
    ///  4. 10 trees by ordered fire (one Tough each, the player's attack order) and 10 by explosions (a grenade's blast on the tile,
    ///     nobody's): each falls in the view, the sound log has the break and the landing of every fall;
    ///  5. the 20 tiles are open (robots; vehicles on the ground tiles), nothing still closes them, each has its stump, the far
    ///     meshes no longer show the trees;
    ///  6. a jeep drives onto every opened ground tile and a robot walks onto every opened swamp tile.
    /// b18_trees_breakable. The second menu writes one line of a running match there (trees felled against rock tiles blasted):
    /// use it at the end of a match of the CPU against itself. Helpers for proof pictures: Places, Hurt, Blast, Attack.
    /// </summary>
    public static class OzV11TreeCount
    {
        const string Tag = "[OpenZ V1.1] b18 trees";
        public const int PerWay = 10, Apart = 8;
        public static string Report = "not run";
        static IEnumerator run; static int waitFrame; static float waitUntil;

        sealed class Pick { public ZObject tree, unit, mover; public int x, y, sx, sy, gx, gy; public bool wet, arrived; }

        [MenuItem("OpenZ/V1.1/Count Trees Felled By Order And By Blast (b18, Play mode, level 16)")]
        public static void Run()
        {
            var g = ZGame.I;
            if (!EditorApplication.isPlaying || !g || g.World == null || g.level != "L16") { Debug.LogWarning(Tag + ": Play mode on level 16 first"); return; }
            if (run != null) { Debug.LogWarning(Tag + ": a run is still going"); return; }
            if (!g.World.IsHumanTeam(g.player) || g.paused) { Debug.LogWarning(Tag + ": a running game against the CPU is needed (not the CPU against itself, not paused)"); return; }
            Report = "running"; run = Count(); waitFrame = 0; waitUntil = 0f; EditorApplication.update += Step;
        }

        [MenuItem("OpenZ/V1.1/Write Trees And Rock Tiles Destroyed So Far (b18, Play mode, level 16)")]
        public static void MatchLineMenu() => Debug.Log(Tag + " " + MatchLine());

        static void Step()
        {
            if (run == null || !EditorApplication.isPlaying) { run = null; EditorApplication.update -= Step; return; }
            if (Time.frameCount < waitFrame || Time.realtimeSinceStartup < waitUntil) return;
            bool more;
            try { more = run.MoveNext(); }
            catch (Exception ex) { Debug.LogException(ex); Report = Tag + " FAIL: exception " + ex.Message; Write("b18_trees_count.txt", "FAIL  the check threw " + ex.GetType().Name + ": " + ex.Message + "\n"); more = false; }
            if (!more) { run = null; EditorApplication.update -= Step; return; }
            if (run.Current is float s) waitUntil = Time.realtimeSinceStartup + s;                  // seconds, real time
            else waitFrame = Time.frameCount + (run.Current is int n ? n : 1);                      // frames
        }

        static string OutDir()
        {
            string all = Path.Combine(OpenZPaths.Records, "milestones"), stage = "v1_1";
            if (Directory.Exists(all))
            {
                var ds = Directory.GetDirectories(all, "v1_1*"); Array.Sort(ds, StringComparer.Ordinal);
                if (ds.Length > 0) stage = Path.GetFileName(ds[ds.Length - 1]);
            }
            string dir = Path.Combine(all, stage, "b18_trees_breakable"); Directory.CreateDirectory(dir);
            return dir;
        }
        static void Write(string file, string text) => File.AppendAllText(Path.Combine(OutDir(), file), text);

        // ------------------------------------------------------------------ places
        static bool Alive(ZObject o) => o != null && !o.Destroyed && o.KillAt < 0;
        static bool Wet(ZWorld W, int x, int y) => W.Map.ClassAt(x, y) == ZClass.Water;

        /// <summary>A unit or a building over these tiles (one tile of room round them).</summary>
        static bool Busy(ZWorld W, int tx, int ty, int size)
        {
            var probe = new ZObject { WidthPix = (size + 2) * 16, HeightPix = (size + 2) * 16 }; probe.SetCords((tx - 1) * 16, (ty - 1) * 16);
            foreach (var o in W.Objects) if (!o.Destroyed && o.Kind != ZKind.MapItem && probe.IntersectsObject(o)) return true;
            return false;
        }

        /// <summary>Can the unit stand here, the tree tile (ox, oy) counted as open? A vehicle: its 2 x 2 tiles from (tx, ty).</summary>
        static bool Fits(ZWorld W, int tx, int ty, int ox, int oy, bool robot)
        {
            for (int j = 0; j < (robot ? 1 : 2); j++)
                for (int i = 0; i < (robot ? 1 : 2); i++)
                {
                    int x = tx + i, y = ty + j;
                    if (x == ox && y == oy) { var c = W.Map.ClassAt(x, y); if (c == ZClass.Ground || c == ZClass.Road || (robot && c == ZClass.Water)) continue; return false; }
                    if (!W.Path.TilePassable(x, y, robot)) return false;
                }
            return true;
        }

        /// <summary>Breadth first from (cx, cy) over the places the unit can stand on: the first free place `want` steps away.</summary>
        static bool Walk(ZWorld W, int cx, int cy, int ox, int oy, bool robot, int want, out int sx, out int sy)
        {
            sx = sy = -1;
            var seen = new HashSet<int> { cy * 1000 + cx }; var q = new List<(int x, int y, int d)> { (cx, cy, 0) };
            for (int i = 0; i < q.Count; i++)
            {
                var (x, y, d) = q[i];
                if (d >= want && !Busy(W, x, y, robot ? 1 : 2)) { sx = x; sy = y; return true; }
                if (d >= want + 4) continue;
                foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    int nx = x + dx, ny = y + dy;
                    if (seen.Add(ny * 1000 + nx) && Fits(W, nx, ny, ox, oy, robot)) q.Add((nx, ny, d + 1));
                }
            }
            return false;
        }

        /// <summary>A 2 x 2 vehicle place that covers the tile once the tree is gone.</summary>
        static bool Cover(ZWorld W, int x, int y, out int cx, out int cy)
        {
            foreach (var (dx, dy) in new[] { (0, 0), (-1, 0), (0, -1), (-1, -1) })
                if (Fits(W, x + dx, y + dy, x, y, false)) { cx = x + dx; cy = y + dy; return true; }
            cx = cy = -1; return false;
        }

        /// <summary>Standing trees for a test, at least <see cref="Apart"/> tiles from each other and from `taken`: on ground with room
        /// for a vehicle on the opened tile (wet = false) or in the swamp (wet = true), each with a free robot tile 3 steps away.</summary>
        static List<Pick> Choose(ZWorld W, int n, bool wet, List<Pick> taken)
        {
            var got = new List<Pick>();
            foreach (var t in W.TreeTiles)
            {
                if (got.Count >= n) break;
                int x = t.LocX >> 4, y = t.LocY >> 4;
                if (!Alive(t) || !t.IsDestroyableImpass || Wet(W, x, y) != wet || OzTreeFall.StateOf(x, y) != 0) continue;
                if (taken.Concat(got).Any(p => Math.Max(Math.Abs(p.x - x), Math.Abs(p.y - y)) < Apart)) continue;
                if (!wet && (!Cover(W, x, y, out int cx, out int cy) || !Walk(W, cx, cy, x, y, false, 3, out _, out _))) continue;
                if (!Walk(W, x, y, x, y, true, 3, out int sx, out int sy)) continue;
                got.Add(new Pick { tree = t, x = x, y = y, sx = sx, sy = sy, wet = wet });
            }
            return got;
        }

        static int Lines(float since, string key) => OzSoundLog.Since(since).Count(e => e.src == "DetailsV2" && e.key == key && e.outcome != "no clip");

        // ------------------------------------------------------------------ the check
        static IEnumerator Count()
        {
            var g = ZGame.I; var W = g.World; var team = g.player; var sb = new StringBuilder();
            var pi = UnityEngine.Object.FindFirstObjectByType<PlayerInput>();
            sb.AppendLine($"---- b18 trees, level {g.level}, game time {W.Time:0} s, game speed {g.gameSpeed:0.##}x, {Screen.width} x {Screen.height}");

            // 1. start
            int listed = ZTreeTilesData.L16.Uids.Length, made = W.TreeTiles.Count, standing = W.TreeTiles.Count(Alive);
            int barriers = W.TreeTiles.Count(t => Alive(t) && W.Path.HasDestroyableBarrier(t.LocX >> 4, t.LocY >> 4));
            int hp = W.TreeHealth, rockHp = (int)(W.S.rock_item_health * ZConst.MAX_UNIT_HEALTH);
            sb.AppendLine(OzV11Kit.Verdict(made == listed && barriers == standing && OzTreeFall.Bound == made && OzTreeFall.Unbound == 0,
                $"start: {made} of {listed} trees are in the rules, {barriers} of the {standing} standing ones close their tile as a barrier that can be destroyed, {OzTreeFall.Bound} have their drawn tree ({OzTreeFall.Unbound} without); {hp} points each, a rock {rockHp}"));
            sb.AppendLine(OzV11Kit.Verdict(OzTreeFall.FarRuns >= OzTreeFall.Bound, $"the runs in the far meshes are loaded for {OzTreeFall.FarRuns} plants (menu OpenZ/L16 Jungle/8b; without one a cell stays on its near objects once its tree fell)"));

            var byOrder = Choose(W, PerWay, false, new List<Pick>());
            var wetOnes = Choose(W, 3, true, byOrder);
            var byBlast = wetOnes.Concat(Choose(W, PerWay - wetOnes.Count, false, byOrder.Concat(wetOnes).ToList())).ToList();
            var lean = Choose(W, 1, false, byOrder.Concat(byBlast).ToList());
            if (byOrder.Count < PerWay || byBlast.Count < PerWay)
            {
                sb.AppendLine(OzV11Kit.Verdict(false, $"only {byOrder.Count} + {byBlast.Count} standing trees with room for the test were found ({PerWay} + {PerWay} needed): start the level again and run it at once"));
                Finish(sb); yield break;
            }

            // 2. the order
            foreach (var p in byOrder) { p.unit = W.CreateRobotGroup("tough", p.sx * 16, p.sy * 16, team, 1); g.SpawnUnitView(p.unit); }
            yield return 3;
            int clicks = 0, taken = 0;
            float t0 = Time.unscaledTime; int falls0 = OzTreeFall.Falls, landed0 = OzTreeFall.Landed, cuts0 = OzTreeFall.FarCuts, forced0 = OzTreeFall.FarForced; double sim0 = W.Time;
            foreach (var p in byOrder)
            {
                if (pi && pi.OrderFor(p.unit, p.tree, p.tree.CenterX, p.tree.CenterY).mode == WpMode.Attack) clicks++;
                bool sent = W.SendWaypoints(team, p.unit, new[] { new ZWaypoint { mode = WpMode.Attack, refId = p.tree.Id, x = p.tree.CenterX, y = p.tree.CenterY } });
                if (sent && p.unit.Waypoints.Count > 0 && p.unit.Waypoints[0].mode == WpMode.Attack && p.unit.Waypoints[0].refId == p.tree.Id) taken++;
            }
            var rifle = W.Objects.FirstOrDefault(o => o.IsUnit && Alive(o) && o.Owner == team && o.CanAttack() && !o.HasExplosives());
            bool noRifle = rifle == null || !rifle.CanAttackObject(byOrder[0].tree);
            sb.AppendLine(OzV11Kit.Verdict(clicks == PerWay && taken == PerWay && noRifle,
                $"a right-click on a tree gives a unit with explosives an attack order ({clicks} of {PerWay}) and the rules accept it ({taken} of {PerWay}); a unit without explosives cannot attack a tree ({(rifle == null ? "not tested: the player has no such unit now" : rifle.Type + (noRifle ? ": refused" : ": NOT refused"))})"));

            // 3. half its points: the damaged stage
            if (lean.Count > 0)
            {
                var l = lean[0]; float tl = Time.unscaledTime; int leans0 = OzTreeFall.Leans;
                l.tree.SetHealth(l.tree.MaxHealth * 2 / 5);
                yield return 1.2f;
                sb.AppendLine(OzV11Kit.Verdict(OzTreeFall.StateOf(l.x, l.y) == 1 && OzTreeFall.Leans > leans0 && Lines(tl, "dtl_tree_creak") >= 1 && Lines(tl, "dtl_tree_crack") >= 1,
                    $"at 2/5 of its points the tree of tile {l.x},{l.y} leans (state {OzTreeFall.StateOf(l.x, l.y)}, 1 = leaning) with its crack and its creak in the sound log ({Lines(tl, "dtl_tree_crack")} and {Lines(tl, "dtl_tree_creak")} line)"));
            }
            else sb.AppendLine(OzV11Kit.Verdict(false, "no further standing tree was found for the damaged stage"));

            // 4a. ordered fire
            while (W.Time - sim0 < 45 && byOrder.Any(p => Alive(p.tree))) yield return 1;
            float until = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < until && OzTreeFall.Landed - landed0 < OzTreeFall.Falls - falls0) yield return 1;
            Way(sb, "ordered fire (one Tough each, the player's attack order)", byOrder, t0, falls0, landed0);

            // 4b. explosions
            float t1 = Time.unscaledTime; int falls1 = OzTreeFall.Falls, landed1 = OzTreeFall.Landed; double sim1 = W.Time;
            for (int i = 0; i < byBlast.Count; i++)
                W.AddMissileForTest(new ZMissile { x = byBlast[i].tree.CenterX, y = byBlast[i].tree.CenterY, damage = (int)(W.S.grenade_damage * ZConst.MAX_UNIT_HEALTH), radius = W.S.grenade_damage_radius,
                                                   team = Team.Neutral, grenade = true, explodeTime = W.Time + 0.5 + 0.3 * i });
            while (W.Time - sim1 < 20 && byBlast.Any(p => Alive(p.tree))) yield return 1;
            until = Time.realtimeSinceStartup + 12f;
            while (Time.realtimeSinceStartup < until && OzTreeFall.Landed - landed1 < OzTreeFall.Falls - falls1) yield return 1;
            Way(sb, "explosions (a grenade's blast on the tile, nobody's)", byBlast, t1, falls1, landed1);

            // 5. the tiles, the stumps, the far meshes
            var all = byOrder.Concat(byBlast).ToList();
            int open = 0, drive = 0, ground = all.Count(p => !p.wet), shut = 0, stumps = 0;
            foreach (var p in all)
            {
                bool robots = W.Path.TilePassable(p.x, p.y, true), vehicles = W.Path.TilePassable(p.x, p.y, false);
                if (robots && (p.wet || vehicles)) open++;
                if (!p.wet && vehicles) drive++;
                if (!robots || W.Path.HasDestroyableBarrier(p.x, p.y)) shut++;
                if (OzTreeFall.HasStump(p.x, p.y)) stumps++;
            }
            sb.AppendLine(OzV11Kit.Verdict(open == all.Count && drive == ground && shut == 0,
                $"{open} of {all.Count} tiles are open (robots on all, vehicles on {drive} of the {ground} ground tiles; the {all.Count - ground} swamp tiles carry robots only, as swamp does); still closed: {shut} (a stump blocks nothing)"));
            sb.AppendLine(OzV11Kit.Verdict(stumps == all.Count, $"{stumps} of {all.Count} felled trees left their stump"));
            int cuts = OzTreeFall.FarCuts - cuts0, forced = OzTreeFall.FarForced - forced0;
            sb.AppendLine(OzV11Kit.Verdict(forced == 0 && cuts >= all.Count, $"far versions: {cuts} plants folded out of their cell's far mesh, {forced} cells had to stay on their near objects instead (this run: {OzTreeFall.Falls - falls0} falls)"));

            // 6. a vehicle drives onto every opened ground tile, a robot walks onto every opened swamp tile
            int staged = 0;
            foreach (var p in all)
            {
                int cx = p.x, cy = p.y;
                if (!p.wet && !Cover(W, p.x, p.y, out cx, out cy)) continue;
                if (!Walk(W, cx, cy, p.x, p.y, p.wet, 3, out int sx, out int sy)) continue;
                p.mover = p.wet ? W.CreateRobotGroup("grunt", sx * 16, sy * 16, team, 1) : W.CreateObject("vehicle", "jeep", sx * 16, sy * 16, team);
                p.gx = p.wet ? p.x * 16 + 8 : cx * 16 + 16; p.gy = p.wet ? p.y * 16 + 8 : cy * 16 + 16;          // a move order names the unit's middle: the robot's on the tile, the jeep's on its 2 x 2 place over the tile
                g.SpawnUnitView(p.mover); staged++;
            }
            yield return 3;
            foreach (var p in all) if (p.mover != null) W.SendWaypoints(team, p.mover, new[] { ZWaypoint.Move(p.gx, p.gy) });
            double sim2 = W.Time;
            while (W.Time - sim2 < 40 && all.Any(p => p.mover != null && !p.arrived))
            {
                foreach (var p in all)
                    if (p.mover != null && !p.arrived) p.arrived = p.wet ? (p.mover.CenterX >> 4) == p.x && (p.mover.CenterY >> 4) == p.y : p.mover.Contains(p.x * 16 + 8, p.y * 16 + 8);
                yield return 1;
            }
            int jeeps = all.Count(p => !p.wet && p.arrived), walkers = all.Count(p => p.wet && p.arrived);
            sb.AppendLine(OzV11Kit.Verdict(staged == all.Count && jeeps == ground && walkers == all.Count - ground,
                $"a jeep drove onto {jeeps} of the {ground} opened ground tiles and a robot walked onto {walkers} of the {all.Count - ground} opened swamp tiles ({staged} of {all.Count} staged)" +
                (all.Any(p => !p.arrived) ? "; not reached: " + string.Join(" ", all.Where(p => !p.arrived).Select(p => p.x + "," + p.y)) : "")));
            sb.AppendLine("the staged units stay in this match: start the level again before anything else is judged in it");
            Finish(sb);
        }

        static void Way(StringBuilder sb, string how, List<Pick> picks, float since, int falls0, int landed0)
        {
            int dead = picks.Count(p => !Alive(p.tree)), view = picks.Count(p => OzTreeFall.StateOf(p.x, p.y) >= 2), down = picks.Count(p => OzTreeFall.StateOf(p.x, p.y) == 3);
            int falls = OzTreeFall.Falls - falls0, landed = OzTreeFall.Landed - landed0, snaps = Lines(since, "dtl_tree_break"), crashes = Lines(since, "dtl_tree_crash");
            sb.AppendLine(OzV11Kit.Verdict(dead == picks.Count && view == picks.Count && down == picks.Count && snaps >= falls && crashes >= landed && falls >= picks.Count,
                $"{dead} of {picks.Count} trees destroyed by {how}; {view} fell in the view, {down} lie on the ground; sound log: {snaps} break and {crashes} landing lines for {falls} falls and {landed} landings in that time" +
                (dead < picks.Count ? "; still standing: " + string.Join(" ", picks.Where(p => Alive(p.tree)).Select(p => p.x + "," + p.y + " (" + p.tree.Health + " points)")) : "")));
        }

        static void Finish(StringBuilder sb)
        {
            string text = sb.ToString();
            int pass = text.Split('\n').Count(l => l.StartsWith("PASS")), fail = text.Split('\n').Count(l => l.StartsWith("FAIL"));
            Report = $"{Tag}: {pass} PASS, {fail} FAIL\n{text}";
            Write("b18_trees_count.txt", text);
            if (fail == 0 && pass > 0) Debug.Log(Report); else Debug.LogError(Report);
        }

        // ------------------------------------------------------------------ a running match, proof helpers
        /// <summary>One line of the match that is running: trees felled against rock tiles blasted (for a match of the CPU against
        /// itself: the CPU never aims at either, both fall to its path blasting and to stray blasts).</summary>
        public static string MatchLine()
        {
            var g = ZGame.I; if (!Application.isPlaying || !g || g.World == null) return "Play mode first";
            var W = g.World;
            int trees = W.TreeTiles.Count, felled = W.TreeTiles.Count(t => !Alive(t));
            int rocks = W.Map.objects.Count(o => o.Type == "rock"), rocksLeft = W.Objects.Count(o => o.Type == "rock" && Alive(o));
            double per = trees > 0 ? 100.0 * felled / trees : 0, perRock = rocks > 0 ? 100.0 * (rocks - rocksLeft) / rocks : 0;
            string line = OzV11Kit.Verdict(per <= 2 * perRock + 5,
                $"match on {g.level}, game time {W.Time:0} s{(W.IsHumanTeam(g.player) ? "" : ", the CPU on both sides")}: {felled} of {trees} trees felled ({per:0.#} of 100), {rocks - rocksLeft} of {rocks} rock tiles blasted ({perRock:0.#} of 100); " +
                $"this check calls it the same range while the trees' share is at most twice the rocks' plus 5; view: {OzTreeFall.Falls} falls, {OzTreeFall.Stumps} stumps, {OzTreeFall.FarCuts} plants folded out of far meshes, {OzTreeFall.FarForced} cells kept near");
            Write("b18_bot_match.txt", line + "\n");
            return line;
        }

        /// <summary>For proof pictures: the first n standing trees with room round them: tile, world place, what stands there.</summary>
        public static string Places(int n = 8, bool wet = false)
        {
            var g = ZGame.I; if (!Application.isPlaying || !g || g.World == null) return "Play mode first";
            var sb = new StringBuilder();
            foreach (var p in Choose(g.World, n, wet, new List<Pick>()))
            {
                OzTreeFall.FootOf(p.x, p.y, out var foot); g.World.DetailPropUid.TryGetValue(p.tree.Id, out var uid);
                sb.AppendLine($"tile {p.x},{p.y} world x {foot.x:0.0} z {foot.z:0.0} {uid} ({(p.wet ? "swamp" : "ground")}); a free robot tile beside it: {p.sx},{p.sy}");
            }
            return sb.Length > 0 ? sb.ToString() : "no standing tree with room was found";
        }

        static ZObject TreeAt(int tx, int ty) { var g = ZGame.I; return g && g.World != null ? g.World.TreeTiles.FirstOrDefault(t => (t.LocX >> 4) == tx && (t.LocY >> 4) == ty) : null; }

        /// <summary>For the picture "hit": the tree of this tile at 2/5 of its points (it leans within a quarter of a second).</summary>
        public static string Hurt(int tx, int ty)
        {
            var t = TreeAt(tx, ty); if (!Alive(t)) return "no standing tree on tile " + tx + "," + ty;
            t.SetHealth(t.MaxHealth * 2 / 5);
            return "tree of tile " + tx + "," + ty + ": " + t.Health + " of " + t.MaxHealth + " points";
        }

        /// <summary>For the pictures "hit" and "fallen" by a real blast: a grenade's blast (nobody's) on the middle of the tile
        /// (fromTx, fromTy) in `after` seconds. On the tree's own tile, or on a tile beside it, it destroys the tree; on a tile that
        /// touches it at a corner it takes about two thirds of its points (the tree leans); from two tiles away it does nothing (a
        /// grenade reaches 30 px).</summary>
        public static string Blast(int fromTx, int fromTy, float after = 0.5f)
        {
            var g = ZGame.I; if (!Application.isPlaying || !g || g.World == null) return "Play mode first";
            var W = g.World;
            W.AddMissileForTest(new ZMissile { x = fromTx * 16 + 8, y = fromTy * 16 + 8, damage = (int)(W.S.grenade_damage * ZConst.MAX_UNIT_HEALTH), radius = W.S.grenade_damage_radius, team = Team.Neutral, grenade = true, explodeTime = W.Time + after });
            return "a grenade's blast on tile " + fromTx + "," + fromTy + " in " + after + " s";
        }

        /// <summary>For the picture "fallen" by ordered fire: the player's attack order on the tree of this tile for a unit of his
        /// (the id OzL16Proof.Spawn returned, e.g. a Tough squad on a free tile beside the tree).</summary>
        public static string Attack(int unitId, int tx, int ty)
        {
            var g = ZGame.I; if (!Application.isPlaying || !g || g.World == null) return "Play mode first";
            var u = g.World.Get(unitId); var t = TreeAt(tx, ty);
            if (u == null || !Alive(t)) return "no such unit, or no standing tree on tile " + tx + "," + ty;
            bool ok = g.World.SendWaypoints(u.Owner, u, new[] { new ZWaypoint { mode = WpMode.Attack, refId = t.Id, x = t.CenterX, y = t.CenterY } }) && u.Waypoints.Count > 0;
            return (ok ? "attack order given to " : "REFUSED for ") + u.Type + " #" + unitId + " on the tree of tile " + tx + "," + ty + (ok ? "" : " (a unit without explosives cannot attack a tree)");
        }
    }
}
