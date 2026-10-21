using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted checks of the level 16 structures (editor only, not in the build). Each prints its lines and a last line
    ///   a  scene and data            edit mode, level 16 scene open: what stands where, the tiles, the whole foot of every piece,
    ///                                the sight grid, levels 2 and 17, the routes
    ///   b  fires and lamps, one day  Play on level 16: the clock is stepped through one full day (72 samples of 10 s), then two
    ///                                short real-time passes at one ruin site for the sounds (about 5 minutes)
    ///   c  breaks                    Play on level 16, a fresh start: the player's attack order on both pyramids (a Tough squad,
    ///                                a heavy tank), both pyramids through their three stages, the 18 huts
    ///   d  pictures                  Play on level 16: the full-screen pictures of the kit's list from the normal game camera,
    ///                                the two pyramids at the closest zoom as well
    ///   e  the clip's staging        Play on level 16, a fresh start: a heavy tank fires at the small pyramid until it has
    ///                                collapsed, then at the hut beside it (for the full-screen 60 fps recording with sound)
    /// </summary>
    public static class OzL16StructuresCheck
    {
        static string OutDir { get { var p = Path.Combine(OpenZPaths.Records, "milestones", "v1_1_20261012", "J7"); Directory.CreateDirectory(p); return p; } }
        sealed class Tally
        {
            public readonly List<string> lines = new List<string>(); public int ok, all; readonly string title;
            public Tally(string t) { title = t; }
            public void Line(bool pass, string text) { all++; if (pass) ok++; lines.Add((pass ? "PASS  " : "FAIL  ") + text); }
            public void Note(string text) { lines.Add("      " + text); }
            public string End(string file)
            {
                string verdict = (ok == all ? "PASS " : "FAIL ") + ok + "/" + all;
                string text = title + "\n" + string.Join("\n", lines) + "\n" + verdict;
                File.WriteAllText(Path.Combine(OutDir, file), text.Replace("\n", "\r\n"));
                if (ok == all) Debug.Log("[OpenZ L16] " + text); else Debug.LogWarning("[OpenZ L16] " + text);
                return verdict;
            }
        }

        // ------------------------------------------------------------------------------------------------ a: scene and data
        [MenuItem("OpenZ/L16 Jungle/Checks/J7 a Structures in the scene and in the level data (edit mode)")]
        public static void SceneMenu() => Scene();

        public static string Scene()
        {
            var T = new Tally("J7 a: level 16 structures, scene and level data");
            var doc = OpenZL16Structures.Load(); var env = GameObject.Find("Environment"); var root = env ? env.transform.Find("L16_Structures") : null;
            if (doc == null || !root) { T.Line(false, "the level 16 scene is not open, or menu 9 was not run, or the placement list is missing"); return T.End("check_a_scene.txt"); }
            var cn = doc.counts; var ruinsT = root.Find("Ruins"); var surT = root.Find("Surround");
            int ruins = doc.ruins.Count(r => r.kind == "ruin" && ruinsT && ruinsT.Find(r.name)), sur = doc.ruins.Count(r => r.kind == "surround" && surT && surT.Find(r.name)), foot = doc.ruins.Count(r => r.kind == "footing" && ruinsT && ruinsT.Find(r.name));
            T.Line(ruins == cn.ruins && cn.ruins == 69, $"ruin pieces in the scene: {ruins} of {cn.ruins} (the plan: 69)");
            T.Line(sur == cn.surround, $"pieces of the surround outside the map: {sur} of {cn.surround}");
            T.Line(foot == cn.footings, $"footings (the plinth in the pool, the pad of the small pyramid): {foot} of {cn.footings}");
            int boxOk = 0, boxAll = 0; float worst = 0f; string worstAt = "";
            foreach (var r in doc.ruins)
            {
                if (r.kind == "footing" || r.shape == "ST_causeway_slabs") continue;
                Transform t = r.kind == "pyramid" ? root.Find(r.uid) : (r.kind == "surround" ? surT : ruinsT)?.Find(r.name);
                var first = t ? (r.kind == "pyramid" ? t.Find("Stages/Intact/LOD0") : (t.Find("LOD0") ?? t.Find("LOD1"))) : null;
                float e = OpenZL16Structures.BoxDiff(r, first); if (e < 0f) continue;
                boxAll++; if (e <= OpenZL16Structures.Tol(r)) boxOk++; if (e > worst) { worst = e; worstAt = r.name; }
            }
            T.Line(boxOk == boxAll && boxAll == cn.ruins + cn.surround + cn.pyramids - 1, $"each stands where the blockout list has it (seated lower by its dz): {boxOk} of {boxAll} boxes within {OpenZL16Structures.BoxTolerance:0.00} m (a piece of the surround: its size x that + 0.15 m), largest {worst:0.000} m at {worstAt}");
            int pyr = 0; foreach (var p in doc.pyramids) { var t = root.Find(p.uid); if (t && t.GetComponent<DetailProp>() && t.GetComponent<L16Pyramid>() && t.Find("Stages/Intact") && t.Find("Stages/Damaged") && t.Find("Stages/RUIN_Mound")) pyr++; }
            T.Line(pyr == 2, $"pyramids with their three stages: {pyr} of 2");
            var ft = root.Find("L16_Fires"); var fta = Resources.Load<TextAsset>(L16Fires.DataPath); var fdoc = fta ? JsonUtility.FromJson<L16Fires.Doc>(fta.text) : null;
            int fires = fdoc?.fires != null && ft ? fdoc.fires.Count(f => ft.Find("Fire_" + f.id + "/Out") && ft.Find("Fire_" + f.id + "/Lit")) : 0;
            string kinds = fdoc?.fires != null ? string.Join(", ", fdoc.fires.GroupBy(f => f.kind).OrderBy(g => g.Key).Select(g => g.Count() + " " + g.Key)) : "-";
            T.Line(fires == 20 && ft && ft.GetComponent<L16Fires>(), $"fires with an out and a lit shape: {fires} of 20 ({kinds}; the plan: 4 campfires, 6 bowls, 6 wall torches, 4 pole torches)");
            var zod = GameObject.Find("ZodObjects"); int huts = doc.huts.Count(h => { var t = zod ? zod.transform.Find(h.name) : null; return t && t.GetComponent<L16Hut>() && t.Find("Hut") && t.Find("RUIN"); });
            T.Line(huts == 18, $"huts of the map with the thatched hut and its fallen state: {huts} of 18");
            // lamp posts: exactly the 15 of the rule, nothing else that the night lights would take for a lamp
            var lampNames = new HashSet<string>(doc.lamps.Select(l => l.name)); int lamps = 0, strangers = 0, placedOk = 0;
            foreach (Transform t in env.transform) { if (t.name.IndexOf("lamp", StringComparison.OrdinalIgnoreCase) < 0) continue; if (lampNames.Contains(t.name)) lamps++; else strangers++; }
            int b = doc.lamps.Count(l => l.kind == "base"), c = doc.lamps.Count(l => l.kind == "crossroads"), f2 = doc.lamps.Count(l => l.kind == "factory");
            T.Line(lamps == 15 && b == 4 && c == 5 && f2 == 6, $"lamp posts in the scene: {lamps} of 15 (list: {b} at the two fort mouths, {f2} at factories, {c} at big crossroads; the rule: 4, 6, 5)");
            T.Line(strangers == 0, $"lamp props anywhere else on the level: {strangers} (must be 0)");
            var gta = Resources.Load<TextAsset>("OpenZ/Maps/L16_grid"); var map = gta ? ZMapData.FromJson(gta.text) : null;
            if (map == null) { T.Line(false, "level data of level 16 not readable"); return T.End("check_a_scene.txt"); }
            var rects = new List<(float x0, float z0, float x1, float z1)>();
            foreach (var o in map.objects)
            {
                int w = o.kind.Contains("fort_front") ? 10 : o.kind.Contains("fort_back") ? 10 : o.kind.Contains("_factory") ? 4 : 0, h = o.kind.Contains("fort_front") ? 12 : o.kind.Contains("fort_back") ? 11 : o.kind.Contains("_factory") ? 5 : 0;
                if (w > 0) rects.Add((o.x * 2f, -(o.y + h) * 2f, (o.x + w) * 2f, -o.y * 2f));
            }
            for (int i = 0; doc.junctions != null && i + 3 < doc.junctions.Length; i += 4) rects.Add((doc.junctions[i], doc.junctions[i + 1], doc.junctions[i + 2], doc.junctions[i + 3]));
            int near = 0; float farthest = 0f;
            foreach (var l in doc.lamps)
            {
                var t = env.transform.Find(l.name); if (!t) continue;
                var rs = t.GetComponentsInChildren<Renderer>(true); if (rs.Length == 0) continue;
                Renderer tallest = rs[0]; foreach (var r in rs) if (r.bounds.size.y > tallest.bounds.size.y) tallest = r;
                float x = tallest.bounds.center.x, z = tallest.bounds.center.z;
                if (Mathf.Abs(x - l.x) <= 0.3f && Mathf.Abs(z - l.z) <= 0.3f) placedOk++;
                float d = rects.Min(r => Mathf.Sqrt(Mathf.Pow(Mathf.Max(Mathf.Max(r.x0 - x, 0f), x - r.x1), 2f) + Mathf.Pow(Mathf.Max(Mathf.Max(r.z0 - z, 0f), z - r.z1), 2f)));
                if (d <= 6f) near++; if (d > farthest) farthest = d;
            }
            T.Line(placedOk == 15, $"each post's foot at its place of the list: {placedOk} of 15 within 0.3 m");
            T.Line(near == 15, $"each post within 6 m of a fort, a factory or a road junction with three long arms: {near} of 15, the farthest {farthest:0.0} m (the draft: 5.5 m)");
            // tiles
            int closed = 0, total = doc.tiles.Length / 3;
            for (int i = 0; i + 2 < doc.tiles.Length; i += 3) if (map.ClassAt(doc.tiles[i], doc.tiles[i + 1]) == ZClass.Impassable) closed++;
            T.Line(closed == total && total == cn.tiles, $"tiles under pyramids, ruin pieces and fires closed in the level data: {closed} of {total} (run apply_j7.py of the kit if not)");
            // rules: levels 2 and 17 never see the pyramid rule; level 16 has the two pyramids on closed tiles
            bool was = ZWorld.L16PyramidsDeviation;
            try
            {
                string settings = Resources.Load<TextAsset>("OpenZ/default_settings").text;
                foreach (var lv in new[] { "L02", "L17" })
                {
                    string mj = Resources.Load<TextAsset>($"OpenZ/Maps/{lv}_grid").text;
                    ZWorld.L16PyramidsDeviation = true; var on = ZWorld.Create(settings, mj, 1, lv == "L17");
                    ZWorld.L16PyramidsDeviation = false; var off = ZWorld.Create(settings, mj, 1, lv == "L17");
                    bool same = on.Objects.Count == off.Objects.Count && on.L16Pyramids.Count == 0 && on.Objects.Zip(off.Objects, (p, q) => p.Id == q.Id && p.Type == q.Type && p.LocX == q.LocX && p.LocY == q.LocY && p.MaxHealth == q.MaxHealth).All(v => v);
                    T.Line(same, $"level {lv.Substring(1)} with the pyramid rule on and off: {on.Objects.Count} and {off.Objects.Count} objects, {(same ? "every one identical" : "DIFFERENT")}");
                }
                ZWorld.L16PyramidsDeviation = true; var w16 = ZWorld.Create(settings, gta.text, 1);
                ZWorld.L16PyramidsDeviation = false; var z16 = ZWorld.Create(settings, gta.text, 1);
                int hp = ZWorld.L16PyramidHitPoints[0], hpSmall = ZWorld.L16PyramidHitPoints[1];
                bool pOk = w16.L16Pyramids.Count == 2 && w16.L16Pyramids[0].MaxHealth == hp && w16.L16Pyramids[1].MaxHealth == hpSmall && w16.L16Pyramids.All(o => o.AttackedByExplosives && o.Owner == Team.Neutral && !o.IsDestroyableImpass) && z16.L16Pyramids.Count == 0 && z16.Objects.Count == w16.Objects.Count - 2;
                T.Line(pOk, $"level 16 rules: {w16.L16Pyramids.Count} pyramids of {hp} and {hpSmall} hit points (a rock tile has 1666), explosives only, not a destroyable block; rule off: {z16.L16Pyramids.Count} pyramids, {w16.Objects.Count - z16.Objects.Count} objects fewer");
                int open = w16.L16PyramidOpenTiles(0) + w16.L16PyramidOpenTiles(1);
                T.Line(open == 0, $"tiles of the two pyramid squares (64 + 36) a unit can still enter: {open} (must be 0)");
                // the WHOLE foot of every pyramid, ruin piece and ground fire (each tile it covers by 35 % or more, not only the tiles
                // this kit closed): no robot may enter it, and the level data itself must close it (a tile that only a rock, hut or
                // tree of the rules blocks would open under the piece when that object is destroyed)
                int ftPieces = 0, ftTiles = 0, ftEnter = 0, ftOpen = 0; var ftAt = new List<string>();
                void CountFoot(string nm, int[] ts)
                {
                    if (ts == null || ts.Length < 2) return;
                    ftPieces++;
                    for (int i = 0; i + 1 < ts.Length; i += 2)
                    {
                        ftTiles++;
                        if (w16.Path.TilePassable(ts[i], ts[i + 1], true)) { ftEnter++; ftAt.Add(nm + " " + ts[i] + "," + ts[i + 1]); }
                        if (map.ClassAt(ts[i], ts[i + 1]) != ZClass.Impassable) ftOpen++;
                    }
                }
                foreach (var rr in doc.ruins) if (rr.kind == "ruin" || rr.kind == "pyramid") CountFoot(rr.name, rr.foot);
                foreach (var fr in doc.fires) CountFoot(fr.name, fr.foot);
                T.Line(ftEnter == 0 && ftOpen == 0 && ftTiles == cn.foot_tiles && ftTiles > 0,
                       $"the whole foot of every pyramid, ruin piece and ground fire: {ftPieces} pieces, {ftTiles} tiles (the list: {cn.foot_tiles}); tiles a robot can enter {ftEnter} (must be 0), tiles the level data itself does not close {ftOpen} (must be 0)" +
                       (ftAt.Count > 0 ? ": " + string.Join("; ", ftAt.Take(10)) : ""));
                // direct fire (the line-of-sight grid of the rules): the standing pyramids block, the low pieces do not
                int lowList = doc.sight_low != null ? doc.sight_low.Length / 2 : -1, lowRules = ZWorld.L16LowTiles.Length / 2, lowBlock = 0, sqBlock = 0;
                bool lowSame = doc.sight_low != null && doc.sight_low.SequenceEqual(ZWorld.L16LowTiles);
                for (int i = 0; i + 1 < ZWorld.L16LowTiles.Length; i += 2) if (w16.Sight.BlockedTile(ZWorld.L16LowTiles[i], ZWorld.L16LowTiles[i + 1])) lowBlock++;
                for (int pk = 0; pk < ZWorld.L16PyramidTiles.Length; pk++)
                {
                    var sq = ZWorld.L16PyramidTiles[pk];
                    for (int x = sq[0]; x < sq[0] + sq[2]; x++) for (int y = sq[1]; y < sq[1] + sq[3]; y++) if (w16.Sight.BlockedTile(x, y)) sqBlock++;
                }
                T.Line(lowSame && lowBlock == 0 && sqBlock == 100,
                       $"direct fire: the tiles under low pieces (campfires, stairs platforms, fallen columns, fire bowls and pole torches on the ground): {lowRules} in the rules, {lowList} in the placement list, the same list: {(lowSame ? "yes" : "NO")}; " +
                       $"of them blocking a shot: {lowBlock} (must be 0); tiles of the two standing pyramid squares that block: {sqBlock} of 100");
                // routes with the game's own path finder: the level data without the structure tiles against the level data as it is
                var before = ZMapData.FromJson(gta.text);
                for (int i = 0; i + 2 < doc.tiles.Length; i += 3) before.classes[doc.tiles[i + 1] * before.width + doc.tiles[i]] = doc.tiles[i + 2];
                ZWorld.L16PyramidsDeviation = true; var w0 = ZWorld.Create(settings, JsonUtility.ToJson(before), 1);
                foreach (bool robot in new[] { true, false })
                {
                    var keys = map.objects.Where(o => o.kind == "map_item:flag" || (o.kind.StartsWith("building:") && !o.kind.Contains("bridge"))).ToList();
                    (int, int)? Spot(ZWorld w, ZMapData.Obj o)
                    {
                        for (int r = 0; r <= 7; r++) for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++)
                        {
                            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue; int x = o.x + dx, y = o.y + dy;
                            bool ok = robot ? w.Path.TilePassable(x, y, true) : w.Path.TilePassable(x, y, false) && w.Path.TilePassable(x + 1, y, false) && w.Path.TilePassable(x, y + 1, false) && w.Path.TilePassable(x + 1, y + 1, false);
                            if (ok) return (x, y);
                        }
                        return null;
                    }
                    bool Route(ZWorld w, (int x, int y) a, (int x, int y) bb)
                    {
                        int h2 = robot ? 8 : 16; w.Path.ForceSearch = true;
                        int id = w.Path.FindPath(a.x * 16 + h2, a.y * 16 + h2, bb.x * 16 + h2, bb.y * 16 + h2, robot, false, out var pts);
                        return id != 0 && pts != null && pts.Count > 0;
                    }
                    int pairs = 0, hadBefore = 0, kept = 0;
                    for (int i = 0; i < keys.Count; i++) for (int j = i + 1; j < keys.Count; j++)
                    {
                        var a0 = Spot(w0, keys[i]); var b0 = Spot(w0, keys[j]); if (a0 == null || b0 == null) continue;
                        pairs++; if (!Route(w0, a0.Value, b0.Value)) continue;
                        hadBefore++;
                        var a1 = Spot(w16, keys[i]); var b1 = Spot(w16, keys[j]);
                        if (a1 != null && b1 != null && Route(w16, a1.Value, b1.Value)) kept++;
                    }
                    T.Line(kept == hadBefore && hadBefore > 0, $"{(robot ? "robot" : "vehicle")} routes between flags and buildings, the game's path finder: {hadBefore} of {pairs} pairs had a route before the structure tiles, {kept} still have one, {hadBefore - kept} cut (must be 0)");
                }
            }
            catch (Exception ex) { T.Line(false, "the rules part threw " + ex.GetType().Name + ": " + ex.Message); }
            finally { ZWorld.L16PyramidsDeviation = was; }
            // seating on the level's ground (the walk surface, equal to the shown ground on dry cells)
            var col = GameObject.Find("COL_Terrain")?.GetComponent<Collider>(); int seatOk = 0, seatAll = 0; float gapWorst = 0f; string gapAt = "";
            if (col)
            {
                Physics.SyncTransforms();
                foreach (var r in doc.ruins)
                {
                    if (r.kind != "ruin" || r.sunk) continue;
                    var first = ruinsT ? ruinsT.Find(r.name + "/LOD0") : null; var mr = first ? first.GetComponent<Renderer>() : null; if (!mr) continue;
                    var bb = mr.bounds; float lowest = float.MaxValue; bool hit = true;
                    foreach (var k in new[] { new Vector2(0, 0), new Vector2(-0.7f, -0.7f), new Vector2(0.7f, -0.7f), new Vector2(-0.7f, 0.7f), new Vector2(0.7f, 0.7f) })
                    {
                        if (col.Raycast(new Ray(new Vector3(bb.center.x + bb.extents.x * k.x, 300f, bb.center.z + bb.extents.z * k.y), Vector3.down), out var h, 600f)) lowest = Mathf.Min(lowest, h.point.y); else hit = false;
                    }
                    if (!hit) continue;
                    seatAll++; float gap = bb.min.y - lowest; if (gap <= 0.05f) seatOk++; if (gap > gapWorst) { gapWorst = gap; gapAt = r.name; }
                }
                T.Line(seatOk == seatAll && seatAll > 0, $"dry ruin pieces with no corner in the air: {seatOk} of {seatAll} tested on the level's ground, the largest gap {Mathf.Max(0f, gapWorst):0.00} m{(gapWorst > 0.05f ? " at " + gapAt : "")}");
            }
            else T.Line(false, "COL_Terrain not found: the seating was not tested");
            return T.End("check_a_scene.txt");
        }

        // ------------------------------------------------------------------------------------------------ the step runner of the Play checks
        static IEnumerator<float> routine; static double wakeAt;
        static void Run(IEnumerator<float> r) { routine = r; wakeAt = 0; EditorApplication.update -= Tick; EditorApplication.update += Tick; }
        static void Tick()
        {
            if (routine == null || !Application.isPlaying) { EditorApplication.update -= Tick; routine = null; return; }
            if (EditorApplication.timeSinceStartup < wakeAt) return;
            bool more; try { more = routine.MoveNext(); } catch (Exception ex) { Debug.LogError("[OpenZ L16] check stopped: " + ex); more = false; }
            if (!more) { EditorApplication.update -= Tick; routine = null; return; }
            wakeAt = EditorApplication.timeSinceStartup + routine.Current;
        }
        static bool Ready(out string why)
        {
            why = !Application.isPlaying ? "enter Play on level 16 first" : !ZGame.I || ZGame.I.World == null || ZGame.I.level != "L16" ? "this is not a running level 16" : !L16Fires.I ? "the fires are not in the scene (menu 9, save, Play again)" : null;
            if (why != null) Debug.LogWarning("[OpenZ L16] " + why);
            return why == null;
        }
        /// <summary>Entries of the sound log with this key from the time `from` up to, not including, the time `to`.</summary>
        static int LoggedIn(float from, float to, string key) => OzSoundLog.Since(from).Count(e => e.key == key && e.t < to);
        static bool VehFree(ZWorld w, int x, int y) => w.Path.TilePassable(x, y, false) && w.Path.TilePassable(x + 1, y, false) && w.Path.TilePassable(x, y + 1, false) && w.Path.TilePassable(x + 1, y + 1, false);
        /// <summary>The first free tile (a robot's, or the top-left of a vehicle's 2 x 2) on the rings rmin..rmax round a tile, in a
        /// fixed order, at least 3 tiles from `avoid`.</summary>
        static (int x, int y)? Ring(ZWorld w, int cx, int cy, bool robot, int rmin, int rmax, (int x, int y)? avoid)
        {
            for (int r = rmin; r <= rmax; r++) for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue; int x = cx + dx, y = cy + dy;
                if (avoid != null && Math.Max(Math.Abs(x - avoid.Value.x), Math.Abs(y - avoid.Value.y)) < 3) continue;
                if (robot ? w.Path.TilePassable(x, y, true) : VehFree(w, x, y)) return (x, y);
            }
            return null;
        }
        static bool Order(ZWorld w, ZObject u, ZObject target) =>
            u != null && target != null && w.SendWaypoints(u.Owner, u, new[] { new ZWaypoint { mode = WpMode.Attack, refId = target.Id, x = target.CenterX, y = target.CenterY } }) && u.Waypoints.Count > 0 && u.Waypoints[0].mode == WpMode.Attack;

        static int Logged(float since, string key, bool playedOnly)
        {
            return OzSoundLog.Since(since).Count(e => e.key == key && (!playedOnly || (e.outcome != null && (e.outcome.StartsWith("PLAY") || e.outcome.StartsWith("LOOP World") || e.outcome.StartsWith("LOOP ") && !e.outcome.Contains("STOP") || e.outcome == "event" || e.outcome == "CombatAudio" || e.outcome == "pool3D"))));
        }

        // ------------------------------------------------------------------------------------------------ b: one day
        [MenuItem("OpenZ/L16 Jungle/Checks/J7 b Fires and lamp posts over one day (Play mode, about 5 minutes)")]
        public static void DayMenu() { if (Ready(out _)) Run(Day()); }

        static IEnumerator<float> Day()
        {
            var T = new Tally("J7 b: level 16 fires and lamp posts over one full day of 12 minutes (the clock stepped in 72 samples of 10 s)");
            var dn = UnityEngine.Object.FindFirstObjectByType<DayNight>(); var nl = NightLights.I; var F = L16Fires.I;
            if (!dn || !nl) { T.Line(false, "day and night clock or night lights missing"); T.End("check_b_day.txt"); yield break; }
            float t0 = dn.timeOfDay; bool ran = dn.running;
            var lamps = nl.Fixtures.Where(f => f.IsLamp && f.LampGO && f.LampGO.name.Contains(L16LampPosts.NamePart)).ToList();
            T.Line(lamps.Count == 15 && nl.Fixtures.Count(f => f.IsLamp) == 15, $"lamp posts the night lights run: {lamps.Count} of level 16, {nl.Fixtures.Count(f => f.IsLamp)} in all (must be 15 and 15); obstacles for robots: {L16LampPosts.Registered}");
            OzL16Proof.Cam(104f, -128f, 230f);                                         // the whole map: every fire and post in view at once
            dn.running = false; dn.timeOfDay = 0f; dn.Apply(0f);
            yield return 4f;                                                        // whatever burned when the check was called is out before the count starts
            float mark = Time.unscaledTime; L16Fires.MaxRealSeen = 0; int budgetMost = 0, upBig = 0, upTorch = 0, dying = 0;
            int fireOk = 0, lampOk = 0, night = 0, nightAll20 = 0, day = 0, dayZero = 0, lampNight = 0, lampNightAll = 0, lampDay = 0, lampDayZero = 0; var wrong = new List<string>();
            for (int k = 0; k < 72; k++)
            {
                float t = Mathf.Repeat(0.005f + k / 72f, 1f); dn.timeOfDay = t; dn.Apply(t);
                { float gameUntil = Time.time + 3.2f; double realCap = EditorApplication.timeSinceStartup + 20.0;      // catching takes 1.2 s, going out 2.5 s, a lamp shows at once: 3.2 s of GAME time
                  while (Time.time < gameUntil && EditorApplication.timeSinceStartup < realCap) yield return 0.1f; }
                // this step's sounds, counted now: the sound log is a ring of 2000 entries that the running match fills as well
                { float upTo = Time.unscaledTime; upBig += LoggedIn(mark, upTo, "amb_fire_lightup_big"); upTorch += LoggedIn(mark, upTo, "amb_fire_lightup_torch"); dying += LoggedIn(mark, upTo, "amb_fire_dying"); mark = upTo; }
                int want = F.Fires.Count(f => !f.dead && NightGrade.LightsOn(t, f.row.onAt, f.row.offAt)), lit = L16Fires.LitNow;
                if (lit == want) fireOk++; else wrong.Add($"t {t:0.000}: {lit} fires lit, the clock says {want}");
                if (want == 20) { night++; if (lit == 20) nightAll20++; }
                if (want == 0) { day++; if (L16Fires.BurningNow == 0) dayZero++; }
                int lwant = lamps.Count(f => NightGrade.LightsOn(t, f.OnAt, f.OffAt)), lon = lamps.Count(f => f.Out > 0.02f);
                if (lon == lwant) lampOk++; else wrong.Add($"t {t:0.000}: {lon} posts lit, the clock says {lwant}");
                if (lwant == 15) { lampNight++; if (lon == 15) lampNightAll++; }
                if (lwant == 0) { lampDay++; if (lon == 0) lampDayZero++; }
                budgetMost = Mathf.Max(budgetMost, nl.Budget.Granted);
            }
            T.Line(fireOk == 72, $"fires lit as the clock says: {fireOk} of 72 samples");
            T.Line(nightAll20 == night && night > 0, $"all 20 fires lit between dusk and dawn: {nightAll20} of {night} night samples");
            T.Line(dayZero == day && day > 0, $"0 fires burning by day: {dayZero} of {day} day samples");
            T.Line(L16Fires.MaxRealSeen <= 8 && L16Fires.MaxRealSeen > 0, $"real fire lights at one time, every frame of the day with all 20 in view: at most {L16Fires.MaxRealSeen} (cap 8); the night budget granted at most {budgetMost} of {nl.maxRealLights} real lights");
            T.Line(lampOk == 72, $"lamp posts lit as the clock says: {lampOk} of 72 samples");
            T.Line(lampNightAll == lampNight && lampNight > 0, $"all 15 posts lit at night: {lampNightAll} of {lampNight} night samples");
            T.Line(lampDayZero == lampDay && lampDay > 0, $"0 posts lit by day: {lampDayZero} of {lampDay} day samples");
            foreach (var w in wrong.Take(8)) T.Note(w);
            yield return 0.3f;                                                      // the frame the last step ended in
            upBig += LoggedIn(mark, float.MaxValue, "amb_fire_lightup_big"); upTorch += LoggedIn(mark, float.MaxValue, "amb_fire_lightup_torch"); dying += LoggedIn(mark, float.MaxValue, "amb_fire_dying");
            T.Line(upBig == 10 && upTorch == 10 && dying == 20, $"sound log over the day, counted after each of the 72 steps: {upBig} light-ups of a campfire or bowl (10), {upTorch} of a torch (10), {dying} dying hisses (20), each with what happened to it");
            // two real-time passes at the small pyramid's site: dusk (the six fires there catch) and dawn (they go out)
            OzL16Proof.Cam(196f, -178f, 46f); float siteFrom = Time.unscaledTime;
            dn.timeOfDay = 0.336f; dn.Apply(0.336f); dn.running = true; int tongues = 0;
            for (int i = 0; i < 120 && (i < 28 || dn.timeOfDay < 0.376f); i++) { yield return 1f; tongues = Mathf.Max(tongues, L16Fires.TonguesDrawn); }   // until the clock has passed the last fire's time (0.370) by the catching time; 28 s when the frames run free
            int siteLit = L16Fires.LitNow; string readout = L16Fires.Readout();
            dn.running = false; dn.timeOfDay = 0.884f; dn.Apply(0.884f); yield return 1f; dn.running = true;
            for (int i = 0; i < 120 && (i < 18 || dn.timeOfDay < 0.910f); i++) yield return 1f;                 // until the clock has passed the last fire's end (0.905) by the dying time
            dn.running = false;
            int pUp = Logged(siteFrom, "amb_fire_lightup_big", true) + Logged(siteFrom, "amb_fire_lightup_torch", true), pLoopC = Logged(siteFrom, "amb_fire_campfire_loop", true), pLoopT = Logged(siteFrom, "amb_fire_torch_loop", true), pDy = Logged(siteFrom, "amb_fire_dying", true);
            T.Line(siteLit == 20 && tongues > 0, $"dusk in real time at the small pyramid: {siteLit} of 20 fires lit after 28 s, flame tongues drawn in a frame: up to {tongues}{(tongues == 0 ? " (the ground fire's tongues are not ready on this level)" : "")}");
            T.Line(pUp >= 3 && pLoopC >= 1 && pLoopT >= 1 && pDy >= 2, $"heard at that site (PLAY lines of the sound log): light-up {pUp} (at least 3), campfire crackle loop {pLoopC} (at least 1), torch loop {pLoopT} (at least 1), dying hiss {pDy} (at least 2)");
            T.Note(readout);
            dn.timeOfDay = t0; dn.Apply(t0); dn.running = ran;
            T.End("check_b_day.txt");
        }

        // ------------------------------------------------------------------------------------------------ c: breaks
        [MenuItem("OpenZ/L16 Jungle/Checks/J7 c Break both pyramids and the 18 huts (Play mode, fresh start)")]
        public static void BreakMenu() { if (Ready(out _)) Run(Break()); }

        static IEnumerator<float> Break()
        {
            var T = new Tally("J7 c: level 16, the pyramids through their three stages and the 18 huts, by the rules' own damage and death");
            var g = ZGame.I; var w = g.World;
            T.Line(w.L16Pyramids.Count == 2 && w.L16Pyramids[0].MaxHealth == ZWorld.L16PyramidHitPoints[0] && w.L16Pyramids[1].MaxHealth == ZWorld.L16PyramidHitPoints[1], $"pyramids in the rules: {w.L16Pyramids.Count} of 2, {ZWorld.L16PyramidHitPoints[0]} and {ZWorld.L16PyramidHitPoints[1]} hit points");
            int bound = L16Pyramid.All.Count(p => p.GetComponent<DetailProp>().SimBacked);
            T.Line(bound == 2, $"drawn pyramids bound to their map items: {bound} of 2");
            int stage1 = 0, stage2 = 0, firesOut = 0, firesAll = 0, stillClosed = 0, shotDown = 0, chips = 0; var shotNotes = new List<string>();
            int crumble = 0, crash = 0, slide = 0, coll = 0;                              // counted per pyramid, right after its stages (the sound log is a ring of 2000 entries)
            for (int k = 0; k < w.L16Pyramids.Count; k++)
            {
                var o = w.L16Pyramids[k]; var lp = L16Pyramid.All.FirstOrDefault(p => p.name == ZWorld.L16PyramidUids[k]); if (!lp) continue;
                OzL16Proof.Cam(lp.transform.position.x, lp.transform.position.z, 46f); yield return 1f;
                OzL16Proof.Shot(Path.Combine(OutDir, $"pyramid_{k}_stage0_intact.png")); yield return 0.5f;
                // the player's own attack order, with the line-of-sight rule as the game runs it: a Tough squad, then a heavy tank. Each stops
                // at its first hit; the hit points are put back to full after each, so the stages below start from a known number
                (int x, int y)? taken = null; float chipFrom = Time.unscaledTime, pyrFrom = chipFrom;
                foreach (string type in new[] { "tough", "heavy" })
                {
                    bool robot = type == "tough"; string who = (robot ? "Tough squad" : "heavy tank") + " at " + ZWorld.L16PyramidUids[k];
                    var at = Ring(w, o.CenterX >> 4, o.CenterY >> 4, robot, o.WidthPix / 32 + 3, o.WidthPix / 32 + 14, taken);
                    if (at == null) { shotNotes.Add(who + ": no free ground near it"); continue; }
                    taken = at; var u = w.Get(OzL16Proof.Spawn(type, g.player == Team.Red, at.Value.x, at.Value.y));
                    yield return 1f;
                    int hp0 = o.Health; double t0 = w.Time, realUntil = EditorApplication.timeSinceStartup + 90.0;
                    bool sent = Order(w, u, o);
                    while (w.Time - t0 < 40 && EditorApplication.timeSinceStartup < realUntil && o.Health >= hp0 && !o.Destroyed) yield return 0.2f;
                    bool hit = sent && o.Health < hp0; if (hit) shotDown++;
                    shotNotes.Add($"{who} from tile {at.Value.x},{at.Value.y}: order {(sent ? "taken" : "REFUSED")}, first hit {(hit ? "after " + (w.Time - t0).ToString("0.0") + " s, " + (hp0 - o.Health) + " points" : "NEVER in 40 s")}");
                    if (u != null) w.SendWaypoints(u.Owner, u, new List<ZWaypoint>());      // stop
                    yield return 2.5f;                                                      // what is in the air lands
                    if (!o.Destroyed && o.Health > 0) o.SetHealth(o.MaxHealth);
                }
                chips += Logged(chipFrom, "dst_rock_chip", false);
                if (lp.Stage == 0 && !o.Destroyed) o.DamageHealth(o.MaxHealth / 2 + 1);     // (a shell may already have taken it to half: then the top is down)
                yield return 2.5f;
                if (lp.Stage == 1) stage1++;
                OzL16Proof.Shot(Path.Combine(OutDir, $"pyramid_{k}_stage1_damaged.png")); yield return 0.5f;
                o.DamageHealth(o.Health); w.UpdateObjectHealth(o, -1); yield return 4f;
                if (lp.Stage == 2 && lp.GetComponent<DetailProp>().Broken) stage2++;
                OzL16Proof.Shot(Path.Combine(OutDir, $"pyramid_{k}_stage2_mound.png")); yield return 0.5f;
                if (w.L16PyramidOpenTiles(k) == 0) stillClosed++;
                foreach (var id in lp.fireIds) { firesAll++; var f = L16Fires.I.Find(id); if (f != null && f.dead) firesOut++; }
                crumble += Logged(pyrFrom, "dst_rock_crumble_medium", false); crash += Logged(pyrFrom, "dst_rock_crash_large", false); slide += Logged(pyrFrom, "dst_l16_pyramid_slide", false); coll += Logged(pyrFrom, "dst_l16_pyramid_collapse", false);
            }
            T.Line(shotDown == 4 && ZWorld.LosDeviation, $"a Tough squad and a heavy tank sent with SendWaypoints(WpMode.Attack) lower each pyramid's hit points: {shotDown} of 4 (line-of-sight rule {(ZWorld.LosDeviation ? "on" : "OFF: switch 'Zod exact' off and run again")}). " + string.Join("; ", shotNotes));
            T.Note($"hits on a pyramid in the sound log during that fire (dst_rock_chip): {chips}{(chips == 0 ? " = a hit on a pyramid has NO sound: say so in the report" : "")}");
            T.Line(stage1 == 2, $"damaged stage at half the hit points: {stage1} of 2");
            T.Line(stage2 == 2, $"rubble mound when the rules destroy it: {stage2} of 2");
            T.Line(stillClosed == 2, $"the square under a destroyed pyramid stays closed: {stillClosed} of 2");
            T.Line(firesOut == firesAll && firesAll == 4, $"fire bowls on the ledges fall and go out with the top: {firesOut} of {firesAll}");
            T.Line(crumble >= 2 && crash >= 2 && slide == 2 && coll == 2, $"sound log: rock crumble {crumble} (at least 2), rock crash {crash} (at least 2), the named slide {slide} (2) and collapse {coll} (2: silent until their files exist)");
            // huts
            var huts = L16Hut.All.ToList(); int lean = 0, broke = 0, open = 0; float hutFrom = Time.unscaledTime;
            var sim = new Dictionary<L16Hut, ZObject>();
            foreach (var h in huts) { foreach (var kv in g.StaticViews) if (kv.Value == h.gameObject) { var o = w.Get(kv.Key); if (o != null) sim[h] = o; } }
            T.Line(huts.Count == 18 && sim.Count == 18, $"huts bound to the map's hut items: {sim.Count} of {huts.Count} (18)");
            OzL16Proof.Cam(104f, -128f, 230f);
            foreach (var kv in sim) kv.Value.DamageHealth(kv.Value.MaxHealth / 2 + 1);
            yield return 1.5f;
            lean = huts.Count(h => h.Leaning);
            int n = 0;
            foreach (var kv in sim)
            {
                if (n++ == 0) { OzL16Proof.Cam(kv.Key.transform.position.x, kv.Key.transform.position.z, 30f); yield return 1f; OzL16Proof.Shot(Path.Combine(OutDir, "hut_leaning.png")); yield return 0.5f; }
                kv.Value.DamageHealth(kv.Value.Health); w.UpdateObjectHealth(kv.Value, -1);
                if (n == 1) { yield return 1.2f; OzL16Proof.Shot(Path.Combine(OutDir, "hut_fallen.png")); }
                yield return 0.3f;
            }
            yield return 2f;
            broke = huts.Count(h => h.Broken);
            foreach (var kv in sim) { BasePropHuts.TryParseHut(kv.Key.name, out int tx, out int ty); if (w.Path.TilePassable(tx, ty, true)) open++; }
            T.Line(lean == 18, $"huts lean at half their hit points: {lean} of 18");
            T.Line(broke == 18, $"huts collapse when the rules destroy them: {broke} of 18");
            T.Line(open == 18, $"hut tiles open again: {open} of 18");
            int wood = Logged(hutFrom, "dst_debris_wood", false), named = Logged(hutFrom, "dst_l16_hut_collapse", false);
            T.Line(wood >= 18 && named == 18, $"sound log: wood debris {wood} (at least 18 requests), the named hut collapse {named} (18: silent until its file exists)");
            T.End("check_c_breaks.txt");
        }

        // ------------------------------------------------------------------------------------------------ c2: one heavy tank, timed
        [MenuItem("OpenZ/L16 Jungle/Checks/J7 c2 One heavy tank fells each pyramid, timed (Play mode, fresh start)")]
        public static void HeavyTimeMenu() { if (Ready(out _)) Run(HeavyTime()); }

        /// <summary>The hit points of the pyramids are set by this count: ONE heavy tank with the player's attack order and its real
        /// shells (no direct damage) needs 35 to 45 s for the big pyramid and 18 to 25 s for the small one, from the order to the fall.</summary>
        static IEnumerator<float> HeavyTime()
        {
            var T = new Tally("J7 c2: level 16, ONE heavy tank with the player's attack order against each pyramid until it falls (its own shells, no direct damage)");
            var g = ZGame.I; var w = g.World;
            double[] lo = { 35, 18 }, hi = { 45, 25 };
            for (int k = 0; k < w.L16Pyramids.Count && k < 2; k++)
            {
                var o = w.L16Pyramids[k]; var lp = L16Pyramid.All.FirstOrDefault(p => p.name == ZWorld.L16PyramidUids[k]);
                string who = "heavy tank at " + ZWorld.L16PyramidUids[k];
                if (lp) OzL16Proof.Cam(lp.transform.position.x, lp.transform.position.z, 46f);
                var at = Ring(w, o.CenterX >> 4, o.CenterY >> 4, false, o.WidthPix / 32 + 2, o.WidthPix / 32 + 14, null);
                if (at == null) { T.Line(false, who + ": no free ground near it"); continue; }
                var u = w.Get(OzL16Proof.Spawn("heavy", g.player == Team.Red, at.Value.x, at.Value.y));
                yield return 1f;
                int hp0 = o.Health, last = hp0, hits = 0, hurt = 0; double t0 = w.Time, first = -1, half = -1, realUntil = EditorApplication.timeSinceStartup + 200.0;
                bool sent = Order(w, u, o);
                while (w.Time - t0 < 100 && EditorApplication.timeSinceStartup < realUntil && !o.Destroyed && o.Health > 0)
                {
                    if (o.Health < last) { hits++; if (first < 0) first = w.Time - t0; last = o.Health; }
                    if (half < 0 && o.Health <= o.MaxHealth / 2) half = w.Time - t0;
                    if (u != null && !u.Destroyed && u.Health < u.MaxHealth) { hurt += u.MaxHealth - u.Health; u.SetHealth(u.MaxHealth); }   // the count is of its shells: the enemy near the big pyramid must not end it
                    yield return 0.02f;
                }
                bool down = o.Destroyed || o.Health <= 0; double t = w.Time - t0; if (down) hits++;
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                T.Line(sent && down && t >= lo[k] && t <= hi[k], string.Format(inv, "{0} from tile {1},{2}: order {3}, first hit after {4:0.0} s, the top down after {5:0.0} s, {6} after {7:0.0} s from the order ({8:0.0} s from the first hit; {9} hits, {10} hit points, {11:0} points a hit on average; the tank alive {14}, points it was healed by the count {15}); wanted {12:0} to {13:0} s",
                    who, at.Value.x, at.Value.y, sent ? "taken" : "REFUSED", first, half, down ? "destroyed" : "NOT destroyed", t, t - first, hits, hp0, hits > 0 ? (hp0 - Math.Max(0, o.Health)) / (double)hits : 0, lo[k], hi[k], u != null && !u.Destroyed ? "yes" : "NO", hurt));
                if (u != null) w.SendWaypoints(u.Owner, u, new List<ZWaypoint>());
                yield return 5f;
                OzL16Proof.Shot(Path.Combine(OutDir, $"pyramid_{k}_felled_by_one_heavy_tank.png")); yield return 0.5f;
            }
            T.End("check_c2_heavy_tank_times.txt");
        }

        // ------------------------------------------------------------------------------------------------ d: pictures
        [MenuItem("OpenZ/L16 Jungle/Checks/J7 d Take the pictures (Play mode, normal game camera, full screen)")]
        public static void PicturesMenu() { if (Ready(out _)) Run(Pictures()); }

        static IEnumerator<float> Pictures()
        {
            var dn = UnityEngine.Object.FindFirstObjectByType<DayNight>(); if (!dn) yield break;
            float t0 = dn.timeOfDay; bool ran = dn.running; dn.running = false;
            var shots = new (string name, float x, float z, float h)[]
            {
                ("site_small_pyramid", 196f, -178f, 46f), ("site_temple_court_west", 13f, -88f, 46f), ("site_gateway_south_west", 12f, -204f, 46f),
                ("site_big_pyramid_pool", 194f, -44f, 46f), ("site_stone_head_east", 194f, -80f, 46f), ("site_column_pair_centre", 135f, -175f, 46f),
                ("site_temple_ridge", 52f, -158f, 46f), ("swamp_ruins_north_west", 57f, -24f, 46f),
                ("lamp_fort_mouth_blue", 104f, -44f, 46f), ("lamp_crossroads_c005", 141.5f, -207.5f, 46f), ("lamp_factory_f012", 165f, -188f, 46f),
                ("site_small_pyramid_h70", 196f, -178f, 70f), ("whole_map", 104f, -128f, 230f),
                ("pyramid_big_closest_h22", 194f, -44f, 22f), ("pyramid_small_closest_h22", 196f, -178f, 22f),          // the closest zoom: how soft does the pyramid's picture read here?
            };
            int n = 0;
            foreach (float tod in new[] { 0.08f, 0.62f })
            {
                dn.timeOfDay = tod; dn.Apply(tod); yield return 5f;                 // lamps warm up, fires catch
                foreach (var s in shots)
                {
                    OzL16Proof.Cam(s.x, s.z, s.h); yield return 1.5f;
                    OzL16Proof.Shot(Path.Combine(OutDir, s.name + (tod < 0.3f ? "_day" : "_night") + ".png")); n++; yield return 0.6f;
                }
            }
            dn.timeOfDay = t0; dn.Apply(t0); dn.running = ran;
            Debug.Log($"[OpenZ L16] J7 pictures: {n} written to {OutDir} (2560 x 1600 when the Game view is set so). LOOK AT EACH ONE.");
        }

        // ------------------------------------------------------------------------------------------------ e: the staging for the clip
        [MenuItem("OpenZ/L16 Jungle/Checks/J7 e Stage the clip (Play mode, fresh start: a heavy tank fires at the small pyramid, then at the hut beside it)")]
        public static void ClipMenu() { if (Ready(out _)) Run(Clip()); }

        /// <summary>What the recording shows, in one camera position and by the game's own fire: 15 s of quiet (start the recording in
        /// that time), a heavy tank of the player fires at the small pyramid (the top slides at half the hit points, then the collapse),
        /// the hut beside it leans at half, the tank fires at it and it falls in. Takes no picture and counts nothing.</summary>
        static IEnumerator<float> Clip()
        {
            var g = ZGame.I; var w = g.World; var o = w.L16Pyramids.Count > 1 ? w.L16Pyramids[1] : null; var lp = L16Pyramid.All.FirstOrDefault(p => p.name == ZWorld.L16PyramidUids[1]);
            if (o == null || !lp || o.Destroyed || lp.Stage != 0) { Debug.LogWarning("[OpenZ L16] J7 e: the small pyramid is not whole any more: start level 16 again"); yield break; }
            var hut = L16Hut.All.Where(h => !h.Broken).OrderBy(h => (h.transform.position - lp.transform.position).sqrMagnitude).FirstOrDefault(); ZObject hutSim = null;
            if (hut) foreach (var kv in g.StaticViews) if (kv.Value == hut.gameObject) hutSim = w.Get(kv.Key);
            var mid = hut ? (lp.transform.position + hut.transform.position) * 0.5f : lp.transform.position;
            OzL16Proof.Cam(mid.x, mid.z, 50f);
            var at = Ring(w, o.CenterX >> 4, o.CenterY >> 4, false, o.WidthPix / 32 + 3, o.WidthPix / 32 + 14, null);
            var tank = at != null ? w.Get(OzL16Proof.Spawn("heavy", g.player == Team.Red, at.Value.x, at.Value.y)) : null;
            Debug.Log($"[OpenZ L16] J7 e: staged at {mid.x:0}, {mid.z:0}, height 50{(tank == null ? " (NO free ground for the tank: the changes will come without a shot)" : "")}. START THE RECORDING NOW: the fire begins in 15 s");
            yield return 15f;
            double until = EditorApplication.timeSinceStartup + 25.0; bool sent = Order(w, tank, o);
            while (lp.Stage == 0 && EditorApplication.timeSinceStartup < until) yield return 0.1f;
            if (lp.Stage == 0 && !o.Destroyed) o.DamageHealth(Math.Max(1, o.Health - o.MaxHealth / 2 + 1));                 // no shot came: the stage is shown all the same
            string how1 = lp.Stage >= 1 ? "by the tank's fire" : "by direct damage (the tank did not hit in 25 s)";
            until = EditorApplication.timeSinceStartup + 25.0;
            while (lp.Stage < 2 && EditorApplication.timeSinceStartup < until) yield return 0.1f;
            string how2 = lp.Stage >= 2 ? "by the tank's fire" : "by direct damage";
            if (lp.Stage < 2 && !o.Destroyed) { o.DamageHealth(o.Health); w.UpdateObjectHealth(o, -1); }
            yield return 9f;
            string how3 = "no hut found";
            if (hut && hutSim != null && !hutSim.Destroyed)
            {
                hutSim.DamageHealth(Math.Max(1, hutSim.Health - hutSim.MaxHealth / 2 + 1)); yield return 3f;                // it leans
                until = EditorApplication.timeSinceStartup + 15.0; Order(w, tank, hutSim);
                while (!hut.Broken && EditorApplication.timeSinceStartup < until) yield return 0.1f;
                how3 = hut.Broken ? "by the tank's fire" : "by direct damage";
                if (!hut.Broken && !hutSim.Destroyed) { hutSim.DamageHealth(hutSim.Health); w.UpdateObjectHealth(hutSim, -1); }
                yield return 7f;
            }
            if (tank != null) w.SendWaypoints(tank.Owner, tank, new List<ZWaypoint>());
            Debug.Log($"[OpenZ L16] J7 e: done. STOP THE RECORDING. The attack order was {(sent ? "taken" : "NOT taken")}; top down {how1}; collapse {how2}; hut {how3}.");
        }
    }
}
