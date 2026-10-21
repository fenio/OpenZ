using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Counted checks of level 16's animals in Play mode (editor only). Each menu runs by itself while the level plays, moves the
    /// normal game camera, spawns and orders its own test units, writes its report to
    /// saved full screen from the normal game camera into the same folder (look at each).
    ///   OpenZ/L16 Jungle/Checks/Animals 1: birds of the open level        any level: eagles, gulls, parrots as they should be there
    ///   OpenZ/L16 Jungle/Checks/Animals 2: crocodile attack and sounds     one staged attack: the sounds in their order, the robot, the helmet
    ///   OpenZ/L16 Jungle/Checks/Animals 3: perched groups, 10 approaches   5 ground and 5 tree groups, a robot squad and a vehicle in turn
    ///   OpenZ/L16 Jungle/Checks/Animals 4: monkeys, 10 approaches          robot at 9 m, vehicle at 13 m, runs sampled every 0.2 s, climbs back
    ///   OpenZ/L16 Jungle/Checks/Animals 5: long watch (start / report)     sampled while a match runs: banks, snakes, eagles, parrots
    ///   OpenZ/L16 Jungle/Checks/Animals 6: a snake hisses at a squad       a squad is walked to a snake that is out: it rears up, the hiss is in the log
    /// Start each in Play on level 16 (check 1 also on levels 2 and 17). One at a time.
    /// GAME SPEED: the checks 1 to 4 and 6 set the game speed to 1 while they run and put it back at their end. The animals' views
    /// (JungleFauna) run on the frame clock, the rules on the frame clock times the game speed: at 4x a wait counted in game seconds
    /// is a quarter of what a monkey or a parrot needs. Only the long watch (5) is meant for 4x: it waits for nothing the views time.
    /// </summary>
    public static class OzL16AnimalsCheck
    {
        static string Folder { get { var p = Path.Combine(OpenZPaths.Records, "milestones", "v1_1_20261012", "J8"); Directory.CreateDirectory(p); return p; } }
        static IEnumerator run; static string running = ""; static float speedWas = -1f;
        static ZGame G => ZGame.I; static ZWorld W => ZGame.I ? ZGame.I.World : null;

        static void Begin(string name, IEnumerator e)
        {
            if (!Application.isPlaying || !G || W == null) { Debug.LogWarning("[OpenZ J8] " + name + ": start Play on the level first"); return; }
            if (run != null) { Debug.LogWarning("[OpenZ J8] " + running + " is still running: one check at a time"); return; }
            run = e; running = name;
            speedWas = G.gameSpeed; G.gameSpeed = 1f;                                        // game seconds = real seconds while the check waits (see the head of this file)
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            Debug.Log("[OpenZ J8] " + name + ": started at game speed 1 (it was " + speedWas.ToString("0.##") + "; put back when the check ends)");
        }

        static void SpeedBack() { if (speedWas > 0f && ZGame.I) ZGame.I.gameSpeed = speedWas; speedWas = -1f; }

        static int lastFrame = -1;
        static void Tick()
        {
            if (!Application.isPlaying) { run = null; EditorApplication.update -= Tick; speedWas = -1f; return; }
            if (Time.frameCount == lastFrame) return; lastFrame = Time.frameCount;
            bool more;
            try { more = run != null && run.MoveNext(); }
            catch (System.Exception ex) { Debug.LogError("[OpenZ J8] " + running + " threw " + ex); more = false; }
            if (!more) { run = null; EditorApplication.update -= Tick; SpeedBack(); }
        }

        sealed class Report
        {
            readonly System.Text.StringBuilder sb = new System.Text.StringBuilder(); int pass, lines; readonly string name;
            public Report(string name)
            {
                this.name = name;
                string speed = !ZGame.I ? "" : speedWas > 0f ? ", run at game speed 1 (it was " + speedWas.ToString("0.##") + " and is put back at the end)" : ", game speed " + ZGame.I.gameSpeed.ToString("0.##");
                sb.AppendLine(name + " on " + (ZGame.I ? ZGame.I.level : "?") + ", " + Screen.width + " x " + Screen.height + speed);
            }
            public void V(bool ok, string what) { lines++; if (ok) pass++; sb.AppendLine((ok ? "PASS  " : "FAIL  ") + what); }
            public void Note(string what) => sb.AppendLine(what);
            public void End()
            {
                sb.Append(pass == lines ? $"PASS {pass}/{lines}" : $"FAIL {pass}/{lines}");
                string path = Path.Combine(Folder, name.Replace(' ', '_').Replace(':', '_').Replace(',', '_') + "_" + (ZGame.I ? ZGame.I.level : "x") + ".txt");
                File.WriteAllText(path, sb.ToString());
                if (pass == lines) Debug.Log("[OpenZ J8] " + sb + "\n-> " + path); else Debug.LogError("[OpenZ J8] " + sb + "\n-> " + path);
            }
        }

        static IEnumerable Wait(double gameSeconds) { double end = W.Time + gameSeconds; while (W != null && W.Time < end) yield return null; }
        static IEnumerable WaitReal(float seconds) { float end = Time.unscaledTime + seconds; while (Time.unscaledTime < end) yield return null; }
        static void Shot(string name) => ScreenCapture.CaptureScreenshot(Path.Combine(Folder, name + ".png"));
        static bool Dry(int tx, int ty) { var c = W.Map.ClassAt(tx, ty); return (c == ZClass.Ground || c == ZClass.Road) && W.Path.TilePassable(tx, ty, true); }
        static bool Dry2(int tx, int ty) => Dry(tx, ty) && Dry(tx + 1, ty) && Dry(tx, ty + 1) && Dry(tx + 1, ty + 1);
        static ZObject Obj(int id) => W.Objects.FirstOrDefault(o => o.Id == id);
        /// <summary>A unit the check spawned: no fire of the running match ends it while it is counted.</summary>
        static void Tough(int id) { var u = Obj(id); if (u == null) return; u.MaxHealth = u.Health = 100000000; foreach (var m in u.Minions) if (m != null) m.MaxHealth = m.Health = 100000000; }
        static float Flat(Vector3 a, Vector3 b) { float dx = a.x - b.x, dz = a.z - b.z; return Mathf.Sqrt(dx * dx + dz * dz); }
        static Vector3 PosOf(ZObject o) { return G.Views.TryGetValue(o.Id, out var v) && v ? v.transform.position : G.CenterWorld(o); }

        /// <summary>A straight walkable line to a place from about `from` m away: the start tile and the last walkable tile before the
        /// place (8 directions; a vehicle needs 2 x 2 tiles all the way).</summary>
        static bool Approach(Vector3 at, float from, bool vehicle, int turn, out int sx, out int sy, out int ex, out int ey)
        {
            sx = sy = ex = ey = 0;
            for (int k0 = 0; k0 < 16; k0++)
            {
                float a = ((k0 + turn * 5) % 16) * Mathf.PI / 8f; var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                bool ok = true; int lx = -1, ly = -1;
                for (float d = from; d >= 0f && ok; d -= 1f)
                {
                    var p = at + dir * d; int tx = Mathf.FloorToInt(p.x / 2f), ty = Mathf.FloorToInt(-p.z / 2f);
                    bool free = vehicle ? Dry2(tx, ty) : Dry(tx, ty);
                    if (free) { lx = tx; ly = ty; if (d >= from - 0.5f) { sx = tx; sy = ty; } }
                    else if (d > 5f) ok = false;                                         // only the last 5 m may be closed (the tree itself)
                    else break;
                }
                if (ok && lx >= 0 && (sx != 0 || sy != 0)) { ex = lx; ey = ly; return true; }
            }
            return false;
        }

        /// <summary>No straight line to a place: a free start tile about `from` m away (16 directions, the turn-th first) and the free tile
        /// nearest to the place (within 8 m). The unit then walks the game's own path between the two.</summary>
        static bool ApproachByPath(Vector3 at, float from, bool vehicle, int turn, out int sx, out int sy, out int ex, out int ey)
        {
            sx = sy = ex = ey = 0; bool haveEnd = false; float best = float.MaxValue;
            int cx = Mathf.FloorToInt(at.x / 2f), cy = Mathf.FloorToInt(-at.z / 2f);
            for (int dy = -4; dy <= 4; dy++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int tx = cx + dx, ty = cy + dy; if (!(vehicle ? Dry2(tx, ty) : Dry(tx, ty))) continue;
                    float d = dx * dx + dy * dy; if (d < best) { best = d; ex = tx; ey = ty; haveEnd = true; }
                }
            if (!haveEnd) return false;
            for (int k0 = 0; k0 < 16; k0++)
            {
                float a = ((k0 + turn * 5) % 16) * Mathf.PI / 8f; var p = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * from;
                int tx = Mathf.FloorToInt(p.x / 2f), ty = Mathf.FloorToInt(-p.z / 2f);
                if (vehicle ? Dry2(tx, ty) : Dry(tx, ty)) { sx = tx; sy = ty; return true; }
            }
            return false;
        }

        /// <summary>The check's own unit is taken out of the level again where it stands: from then on it counts for no animal (a test
        /// unit left standing kept groups from sitting down at a spot near it and troops from climbing back).</summary>
        static void Remove(int id)
        {
            var u = Obj(id); if (u == null) return;
            var all = new List<ZObject> { u }; foreach (var m in u.Minions) if (m != null) all.Add(m);
            foreach (var m in all) { if (m.Destroyed) continue; m.MaxHealth = m.Health = 1; m.DamageHealth(2); W.UpdateObjectHealth(m, -1); }
        }

        // ================================================================== 1 birds of the open level
        [MenuItem("OpenZ/L16 Jungle/Checks/Animals 1: birds of the open level (Play; levels 2, 16, 17)")]
        static void BirdsMenu() => Begin("Animals 1 birds", Birds());

        static IEnumerator Birds()
        {
            var r = new Report("Animals 1 birds of the open level");
            foreach (var _ in WaitReal(2f)) yield return null;
            int eagles = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Count(t => t.name.StartsWith("Eagle") && t.parent && t.parent.name == "AmbientLife");
            int gulls = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Count(t => t.name.StartsWith("Seagull") && t.parent && t.parent.name == "AmbientLife");
            int eagleCries = OzSoundLog.Since(0f).Count(e => e.key != null && (e.key.Contains("crow") || e.key.Contains("eagle")));
            string lv = G.level;
            if (lv == "L16")
            {
                r.V(eagles == 0 && gulls == 0, $"level 16: {eagles} eagle objects, {gulls} gull objects (wanted 0 and 0)");
                r.V(eagleCries == 0, $"level 16: {eagleCries} eagle cries in the sound log so far (wanted 0)");
                r.V(JungleFauna.FlyingParrots == JungleFaunaData.ParrotRoutes.Length, $"level 16: {JungleFauna.FlyingParrots} parrots in the air (the eagle's count: {JungleFaunaData.ParrotRoutes.Length})");
                r.V(JungleFauna.MonkeysTotal == 18 && JungleFaunaData.TroopTrees.Length == 6, $"level 16: {JungleFauna.MonkeysTotal} monkeys in {JungleFaunaData.TroopTrees.Length} troops (wanted 18 in 6)");
                r.V(JungleFauna.SnakesTotal == 6, $"level 16: {JungleFauna.SnakesTotal} snakes at their dens (wanted 6)");
                r.V(W.Crocs.Count == ZCrocData.Places.Length, $"level 16: {W.Crocs.Count} crocodiles (the table has {ZCrocData.Places.Length})");
                r.Note(JungleFauna.Status()); r.Note(CrocodileView.Status());
            }
            else
            {
                r.V(lv == "L02" ? eagles == 3 && gulls == 0 : eagles == 0 && gulls == 5, $"{lv}: {eagles} eagle objects, {gulls} gull objects (as before: Desert 3 eagles, Car Park 5 gulls)");
                r.V(!JungleFauna.I && !CrocodileView.I && W.Crocs.Count == 0, $"{lv}: no jungle animal runs here (parrots, monkeys, snakes: {(JungleFauna.I ? "RUNNING" : "none")}; crocodiles: {W.Crocs.Count})");
            }
            r.End();
        }

        // ================================================================== 2 one attack: the sounds in their order
        [MenuItem("OpenZ/L16 Jungle/Checks/Animals 2: crocodile attack and its sounds (Play, level 16)")]
        static void AttackMenu() => Begin("Animals 2 crocodile attack", Attack());

        static IEnumerator Attack()
        {
            var r = new Report("Animals 2 crocodile attack and its sounds");
            if (G.level != "L16" || W.Crocs.Count == 0 || !CrocodileView.I) { r.V(false, "level 16 with its crocodiles is not running"); r.End(); yield break; }
            // the pool with the most attack water; a robot on hold position in the middle of it
            int pool = Enumerable.Range(0, W.CrocPoolCount).OrderByDescending(p => W.Crocs.Any(c => c.Pool == p) ? W.CrocPoolAttackM2(p) : -1).First();
            long sx = 0, sy = 0, n = 0; var cells = new List<Vector2Int>();
            for (int y = 2; y < W.Map.HeightPix; y += 4) for (int x = 2; x < W.Map.WidthPix; x += 4) if (W.CrocAttackWater(x, y) && W.CrocPoolAt(x, y) == pool) { sx += x; sy += y; n++; cells.Add(new Vector2Int(x, y)); }
            if (n == 0) { r.V(false, "no attack water on this level"); r.End(); yield break; }
            // the cell with the most attack water round it (within 5 m): the widest part of the pool's attack water. (Its middle may be the
            // pyramid: that pool is a ring, and close to the pyramid's foot a hunt found no side to come from.) Then cells 6 m and 12 m from it
            {
                var set = new HashSet<long>(); foreach (var c in cells) set.Add(((long)c.x << 20) | (uint)c.y);
                var dens = new Dictionary<Vector2Int, int>();
                foreach (var c in cells) { int k = 0; for (int dy = -40; dy <= 40; dy += 4) for (int dx = -40; dx <= 40; dx += 4) if (set.Contains(((long)(c.x + dx) << 20) | (uint)(c.y + dy))) k++; dens[c] = k; }
                cells.Sort((p, q) => dens[q].CompareTo(dens[p]));
            }
            bool taken = false; string order = ""; int tries = 0;
            string[] want = { "critter_croc_warning", "critter_croc_wake_loop", "critter_croc_jaw_snap", "critter_croc_thrash_loop", "critter_croc_bubbles" };
            while (!taken && tries < 3)
            {
                tries++;
                var cell = cells[0];
                if (tries > 1) { int far = (tries - 1) * 48; var c2 = cells.FirstOrDefault(c => (c.x - cells[0].x) * (c.x - cells[0].x) + (c.y - cells[0].y) * (c.y - cells[0].y) >= far * far); if (c2 != default(Vector2Int)) cell = c2; }
                int px = cell.x, py = cell.y;
                OzL16Proof.Cam(px / 8f, -py / 8f, 22f);
                var robot = W.CreateRobotGroup("grunt", px - 8, py - 8, Team.Red, 1); G.SpawnUnitView(robot);
                W.SendHold(Team.Red, robot);
                float t0 = Time.unscaledTime; int kills0 = W.CrocKills, hunts0 = W.CrocHunts;
                foreach (var _ in Wait(ZWorld.CrocWetSeconds + 0.4)) yield return null;
                bool started = W.CrocHunts > hunts0 || W.CrocStartHuntForTest(robot);
                if (!started) { r.Note($"try {tries}: no hunt could be started on the robot at {px / 8f:0.0}, {-py / 8f:0.0} (attack water there: {W.CrocAttackWater(robot.CenterX, robot.CenterY)}); another place"); foreach (var _ in Wait(45)) yield return null; continue; }
                bool shot1 = false, shot2 = false, shot3 = false;
                double end = W.Time + 25;
                while (W.Time < end && (W.CrocHuntsActive > 0 || W.CrocHunts == hunts0))
                {
                    var c = W.Crocs.FirstOrDefault(x => x.PreyId == robot.Id || (x.Took && x.Busy));
                    if (c != null && c.State == ZCrocState.Stalk && !shot1) { shot1 = true; Shot($"croc_attack_{tries}_1_stalk_wake_h22"); }
                    if (c != null && c.State == ZCrocState.Lunge && c.Took && !shot2) { shot2 = true; Shot($"croc_attack_{tries}_2_bite_h22"); }
                    if (c != null && c.State == ZCrocState.Thrash && W.Time - c.Since > 1.0 && !shot3) { shot3 = true; Shot($"croc_attack_{tries}_3_roll_h22"); }
                    yield return null;
                }
                taken = W.CrocKills > kills0;
                if (!taken) { r.Note($"try {tries}: the hunt ended without a bite (gave up before the rise {W.CrocMissBefore}, missed after it {W.CrocMissAfter}); another place"); foreach (var _ in Wait(45)) yield return null; continue; }
                foreach (var _ in Wait(7)) yield return null;                               // the bubbles, the helmet, the burp
                Shot($"croc_attack_{tries}_4_helmet_h22");
                yield return null; yield return null;
                OzL16Proof.Cam(px / 8f, -py / 8f, 40f);                                       // the helmet must read from camera height 40
                foreach (var _ in Wait(0.6)) yield return null;
                Shot($"croc_attack_{tries}_5_helmet_h40");
                yield return null; yield return null;
                var log = OzSoundLog.Since(t0).Where(e => e.src == "L16").ToList();
                var first = want.Select(k => log.FindIndex(e => e.key == k)).ToList();
                bool inOrder = first.All(i => i >= 0) && Enumerable.Range(0, first.Count - 1).All(i => first[i] < first[i + 1]);
                order = string.Join(" > ", log.Select(e => e.key + (e.outcome.StartsWith("DROP") ? " (silent: no file)" : e.outcome.StartsWith("LOOP STOP") ? " (stop)" : "")));
                r.V(inOrder, "growl, wake, snap, roll, bubbles are in the sound log in that order" + (inOrder ? "" : " (first at lines " + string.Join(", ", first) + ")"));
                int silent = log.Count(e => want.Contains(e.key) && e.outcome.StartsWith("DROP"));
                r.V(silent == 0, $"{silent} of the five are silent for want of a file (wanted 0)");
                r.V(!(G.Views.TryGetValue(robot.Id, out var rv) && rv && rv.gameObject.activeSelf), "the robot is gone under with the animal (its view is hidden)");
                r.V(CrocodileView.PartsFloated >= 1, $"its helmet came up afterwards ({CrocodileView.PartsFloated} so far)");
                r.Note("sound log of the attack: " + order);
            }
            r.V(taken, $"a robot on hold position in the middle of the attack water was taken ({tries} tr{(tries == 1 ? "y" : "ies")})");
            r.End();
        }

        // ================================================================== 3 perched groups
        [MenuItem("OpenZ/L16 Jungle/Checks/Animals 3: perched groups, 10 approaches (Play, level 16)")]
        static void PerchedMenu() => Begin("Animals 3 perched groups", Perched());

        static IEnumerator Perched()
        {
            var r = new Report("Animals 3 perched groups, 10 approaches");
            if (G.level != "L16" || !JungleFauna.I) { r.V(false, "level 16 with its animals is not running"); r.End(); yield break; }
            var centre = new List<Vector3>(); var tree = new List<bool>(); var birds = new List<int>();
            int took = 0, groundOk = 0, treeOk = 0, soundOk = 0, appeared = 0, hiddenOk = 0, groundN = 0, treeN = 0; var dists = new List<string>();
            for (int i = 0; i < 10; i++)
            {
                JungleFauna.Groups(centre, tree, birds);
                if (i >= centre.Count) break;
                var at = centre[i]; bool isTree = tree[i]; if (isTree) treeN++; else groundN++;
                // while the camera looks elsewhere: no bird object anywhere out of its view and rim
                OzL16Proof.Cam(at.x > 104f ? 20f : 188f, at.z > -128f ? -236f : -20f, 30f);
                foreach (var _ in Wait(0.6)) yield return null;
                JungleFauna.Groups(centre, tree, birds);
                if (birds[i] == 0) hiddenOk++;
                // the camera comes: the group is there
                OzL16Proof.Cam(at.x, at.z, 34f);
                foreach (var _ in Wait(1.0)) yield return null;
                JungleFauna.Groups(centre, tree, birds);
                at = centre[i];
                if (birds[i] > 0) appeared++;
                if (i == 0 || i == 5) Shot($"perched_{(isTree ? "tree" : "ground")}_group_{i}_1_sitting_h34");
                bool vehicle = (i & 1) == 1;
                int sx = 0, sy = 0, ex = 0, ey = 0; float lineM = 0f;
                foreach (float from in new[] { 30f, 22f, 16f }) if (Approach(new Vector3(at.x, 0f, at.z), from, vehicle, i, out sx, out sy, out ex, out ey)) { lineM = from; break; }
                bool byPath = false;
                if (lineM <= 0f)
                {   // no straight line: the unit walks the game's own path from a free tile 30 m away to the free tile nearest the group
                    if (!ApproachByPath(new Vector3(at.x, 0f, at.z), 30f, vehicle, i, out sx, out sy, out ex, out ey))
                    {
                        if (!vehicle || !ApproachByPath(new Vector3(at.x, 0f, at.z), 30f, false, i, out sx, out sy, out ex, out ey)) { r.Note($"group {i}: no straight walkable line to it and no free tile within 8 m of it; skipped"); continue; }
                        vehicle = false; r.Note($"group {i}: a jeep finds no free 2 x 2 tiles within 8 m of it: a squad goes instead");
                    }
                    byPath = true; lineM = 30f; r.Note($"group {i}: no straight walkable line of 30, 22 or 16 m: the unit walks the game's path from tile {sx},{sy} to tile {ex},{ey}");
                }
                else if (lineM < 30f) r.Note($"group {i}: approached on a straight line of {lineM:0} m (none of 30 m)");
                int id = OzL16Proof.Spawn(vehicle ? "jeep" : "grunt", true, sx, sy);
                Tough(id); string sent = OzL16Proof.Move(id, ex, ey);
                if (sent.StartsWith("REFUSED")) r.Note($"group {i}: the move order was refused ({sent})");
                int logged = JungleFauna.TakeOffLog.Count; float t0 = Time.unscaledTime; double end = W.Time + (byPath ? 90 : 45); float nearest = 999f;
                while (W.Time < end && !JungleFauna.TakeOffLog.Skip(logged).Any(e => e.group == i)) { var tu = Obj(id); if (tu != null) nearest = Mathf.Min(nearest, Flat(PosOf(tu), at)); yield return null; }
                var hit = JungleFauna.TakeOffLog.Skip(logged).Where(e => e.group == i).ToList();
                if (hit.Count == 0)
                {   // nothing took off (the group may have been up already, frightened by a unit of the running match): once more, from another side, 25 s later
                    Remove(id); foreach (var _ in Wait(25)) yield return null;
                    int sx2, sy2, ex2, ey2;
                    if (Approach(new Vector3(at.x, 0f, at.z), lineM, vehicle, i + 2, out sx2, out sy2, out ex2, out ey2))
                    {
                        r.Note($"group {i}: no take-off at the first approach; approached once more from another side");
                        int id2 = OzL16Proof.Spawn(vehicle ? "jeep" : "grunt", true, sx2, sy2); Tough(id2); OzL16Proof.Move(id2, ex2, ey2);
                        logged = JungleFauna.TakeOffLog.Count; t0 = Time.unscaledTime; end = W.Time + 45;
                        while (W.Time < end && !JungleFauna.TakeOffLog.Skip(logged).Any(e => e.group == i)) { var tu = Obj(id2); if (tu != null) nearest = Mathf.Min(nearest, Flat(PosOf(tu), at)); yield return null; }
                        hit = JungleFauna.TakeOffLog.Skip(logged).Where(e => e.group == i).ToList();
                        Remove(id2);
                    }
                }
                if (hit.Count > 0)
                {
                    took++;
                    float want = isTree ? JungleFauna.I.treeFright : JungleFauna.I.groundFright; float d = hit[0].dist;
                    dists.Add($"{(isTree ? "tree" : "ground")} {(vehicle ? "jeep" : "squad")} {d:0.0} m");
                    if (Mathf.Abs(d - want) <= 1.5f) { if (isTree) treeOk++; else groundOk++; }
                    var log = OzSoundLog.Since(t0).Where(e => e.src == "L16").ToList();
                    bool burst = log.Any(e => e.key == "critter_parrot_flutter" && Mathf.Abs(e.t - hit[0].time) <= 0.06f && e.outcome.StartsWith("PLAY"));
                    bool squawk = log.Any(e => e.key == "amb_l16_parrot_squawk" && Mathf.Abs(e.t - hit[0].time) <= 0.06f && e.outcome.StartsWith("PLAY"));
                    if (burst && squawk) soundOk++;
                    if (i == 0 || i == 5) { foreach (var _ in Wait(0.7)) yield return null; Shot($"perched_{(isTree ? "tree" : "ground")}_group_{i}_2_take_off_h34"); foreach (var _ in Wait(2.5)) yield return null; Shot($"perched_{(isTree ? "tree" : "ground")}_group_{i}_3_ring_h34"); }
                }
                else
                {
                    dists.Add($"{(isTree ? "tree" : "ground")} {(vehicle ? "jeep" : "squad")} NO TAKE-OFF");
                    JungleFauna.Groups(centre, tree, birds);
                    r.Note($"group {i} ({(isTree ? "tree" : "ground")} group, approached by a {(vehicle ? "jeep" : "squad")}{(byPath ? " on a path" : "")}): NO TAKE-OFF. The test unit came as near as {nearest:0.0} m (the group takes off at {(isTree ? JungleFauna.I.treeFright : JungleFauna.I.groundFright):0} m); the group now has {birds[i]} bird objects and its place is {centre[i].x:0.0}, {centre[i].z:0.0} (it was {at.x:0.0}, {at.z:0.0} when the camera came)");
                }
                Remove(id);                                                                   // the test unit is taken out again: it neither disturbs the next group nor stands at a spot
                foreach (var _ in Wait(3)) yield return null;
            }
            r.Note("take-offs: " + string.Join("; ", dists));
            r.V(took == 10, $"{took} of 10 approached groups took off (wanted 10 of 10)");
            r.V(groundOk == groundN && groundN == 5, $"{groundOk} of {groundN} ground groups took off with the unit {JungleFauna.I.groundFright:0} m away, within 1.5 m (wanted 5 of 5)");
            r.V(treeOk == treeN && treeN == 5, $"{treeOk} of {treeN} tree groups took off with the unit {JungleFauna.I.treeFright:0} m away, within 1.5 m (wanted 5 of 5)");
            r.V(soundOk == took && took > 0, $"{soundOk} of {took} take-offs have the wing burst and the squawk in the sound log on the take-off frame");
            r.V(hiddenOk == 10, $"{hiddenOk} of 10 looks elsewhere: the group had 0 bird objects while it was outside the camera's view and its 15 m rim");
            r.V(appeared == 10, $"{appeared} of 10 times the group was there when the camera came");
            r.End();
        }

        // ================================================================== 4 monkeys
        [MenuItem("OpenZ/L16 Jungle/Checks/Animals 4: monkeys, 10 approaches (Play, level 16)")]
        static void MonkeysMenu() => Begin("Animals 4 monkeys", MonkeysRun());

        static IEnumerator MonkeysRun()
        {
            var r = new Report("Animals 4 monkeys, 10 approaches");
            if (G.level != "L16" || !JungleFauna.I) { r.V(false, "level 16 with its animals is not running"); r.End(); yield break; }
            r.V(JungleFauna.MonkeysTotal == 18 && JungleFauna.MonkeysSitting == 18, $"{JungleFauna.MonkeysTotal} monkeys in {JungleFaunaData.TroopTrees.Length} troops at the start, {JungleFauna.MonkeysSitting} sitting (wanted 18 in 6, all sitting)");
            var pos = new List<Vector3>(); var st = new List<int>(); var before = new List<int>();
            var rock = new HashSet<int>();
            foreach (var o in W.Objects)
            {
                if (o.Kind == ZKind.Building && !o.IsBridge) for (int y = o.LocY / 16; y < (o.LocY + o.HeightPix) / 16; y++) for (int x = o.LocX / 16; x < (o.LocX + o.WidthPix) / 16; x++) rock.Add(y * 1000 + x);
                if (o.Kind == ZKind.MapItem && o.Type == "rock" && !o.Destroyed) rock.Add((o.LocY / 16 + 2) * 1000 + o.LocX / 16);
            }
            int jumped = 0, distOk = 0, samples = 0, onWater = 0, inRock = 0, ends = 0, endsOk = 0; var dists = new List<string>(); double nextSample = 0;
            int alarm0 = Asked("critter_monkey_alarm"), land0 = Asked("critter_monkey_land"), jump0 = Asked("critter_monkey_jump");
            System.Action sample = () =>
            {
                if (W.Time < nextSample) return; nextSample = W.Time + 0.2;
                before.Clear(); before.AddRange(st);
                JungleFauna.Monkeys(pos, st);
                for (int k = 0; k < pos.Count; k++)
                {
                    int s = st[k]; var p = pos[k]; int tx = Mathf.FloorToInt(p.x / 2f), ty = Mathf.FloorToInt(-p.z / 2f);
                    bool onGround = p.y - G.GroundY(p.x, p.z) < 0.6f;
                    if ((s == 3 || s == 5) && onGround)
                    {   // running: never on a water tile, never inside a rock or building tile
                        samples++;
                        if (W.Map.ClassAt(tx, ty) == ZClass.Water) onWater++;
                        if (rock.Contains(ty * 1000 + tx)) inRock++;
                    }
                    if (s == 4 && k < before.Count && before[k] == 3)
                    {   // a run just ended: in dense trees (a closed tile that is no rock or building) or beyond the map's edge
                        ends++;
                        bool edge = tx < 0 || ty < 0 || tx >= W.Map.width || ty >= W.Map.height || tx == 0 || ty == 0 || tx == W.Map.width - 1 || ty == W.Map.height - 1;
                        bool trees = !edge && !W.Path.TilePassable(tx, ty, true) && !rock.Contains(ty * 1000 + tx);
                        if (edge || trees) endsOk++;
                    }
                }
            };
            // The rule: monkeys keep away while a unit is near (they climb back only with no unit within 14 m of their tree). A troop
            // that has units of the match inside 14 m when the check begins (the player's start units beside his fort) would jump at
            // the first approach and never come back while those stand there: such a troop is left out, and said.
            var free = new List<int>(); var leftOut = new List<string>();
            for (int tt = 0; tt < JungleFaunaData.TroopTrees.Length; tt++)
            {
                var f0 = new Vector3(JungleFaunaData.TroopTrees[tt].x, 0f, JungleFaunaData.TroopTrees[tt].y); float nearU = 999f; string who = "";
                foreach (var o in W.Objects) if (o.IsUnit && !o.Destroyed) { float d = Flat(PosOf(o), f0); if (d < nearU) { nearU = d; who = o.Type; } }
                if (nearU < 14f) leftOut.Add($"troop {tt} (a {who} of the match stands {nearU:0.0} m from its tree)"); else free.Add(tt);
            }
            if (leftOut.Count > 0) r.Note("left out, units inside 14 m of their tree at the start: " + string.Join("; ", leftOut));
            int firstRound = free.Count, asked = 0;
            var orderL = new List<int>(free); for (int k = 0; k < free.Count && k < 4; k++) orderL.Add(free[k]);
            int[] order = orderL.ToArray();
            for (int i = 0; i < order.Length; i++)
            {
                int t = order[i]; bool second = i >= firstRound; int slot = second ? i - firstRound : i;
                bool vehicle = (slot & 1) == 1 ^ second;                                        // each of the first four free troops once by a robot, once by a vehicle
                var foot = new Vector3(JungleFaunaData.TroopTrees[t].x, 0f, JungleFaunaData.TroopTrees[t].y);
                if (i == firstRound)
                {   // the troops of the first round must be back up first: quiet for their 40 to 70 s
                    OzL16Proof.Cam(foot.x, foot.z, 34f);
                    double until = W.Time + 180;
                    while (W.Time < until && JungleFauna.MonkeysSitting < 18) { sample(); yield return null; }
                    r.Note($"before the second round: {JungleFauna.MonkeysSitting} of 18 monkeys sit again ({(W.Time - (until - 180)):0} game seconds waited; every test unit of the first round was taken out)");
                }
                {   // the same question again now: the player's forts and factories keep building, and new units gather near some trees
                    float nearNow = 999f; string whoNow = "";
                    foreach (var o in W.Objects) if (o.IsUnit && !o.Destroyed) { float d = Flat(PosOf(o), foot); if (d < nearNow) { nearNow = d; whoNow = o.Type; } }
                    if (nearNow < 14f) { r.Note($"troop {t}: left out at its {(second ? "second" : "first")} approach: a {whoNow} of the match stands {nearNow:0.0} m from its tree by now (game time {W.Time:0} s)"); continue; }
                }
                asked++;
                OzL16Proof.Cam(foot.x, foot.z, 34f);
                foreach (var _ in Wait(0.8)) yield return null;
                if (i == 0) Shot("monkeys_troop_0_1_sitting_h34");
                int sx, sy, ex, ey; bool byPath = false;
                if (!Approach(foot, 30f, vehicle, i, out sx, out sy, out ex, out ey))
                {
                    if (!ApproachByPath(foot, 30f, vehicle, i, out sx, out sy, out ex, out ey)) { r.Note($"troop {t}: no straight walkable line of 30 m to its tree and no free tile within 8 m of it; skipped"); continue; }
                    byPath = true; r.Note($"troop {t}: no straight walkable line of 30 m: the unit walks the game's path from tile {sx},{sy} to tile {ex},{ey}");
                }
                int sat0 = 0; { JungleFauna.Monkeys(pos, st); for (int k = 0; k < pos.Count; k++) if (Flat(pos[k], foot) < 8f && pos[k].y - G.GroundY(pos[k].x, pos[k].z) > 1f) sat0++; }
                int id = OzL16Proof.Spawn(vehicle ? "light" : "grunt", true, sx, sy);
                Tough(id); string sent = OzL16Proof.Move(id, ex, ey);
                if (sent.StartsWith("REFUSED")) r.Note($"troop {t}: the move order was refused ({sent})");
                int runs0 = JungleFauna.MonkeyRuns; double end = W.Time + (byPath ? 90 : 45); float dAt = -1f, nearest = 999f;
                while (W.Time < end && JungleFauna.MonkeyRuns == runs0) { sample(); var tu = Obj(id); if (tu != null) nearest = Mathf.Min(nearest, Flat(PosOf(tu), foot)); yield return null; }
                if (JungleFauna.MonkeyRuns > runs0)
                {
                    jumped++;
                    var o = Obj(id); dAt = o != null ? Flat(PosOf(o), foot) : -1f;
                    float want = vehicle ? JungleFauna.I.monkeyVehicle : JungleFauna.I.monkeyRobot;
                    if (dAt >= 0f && Mathf.Abs(dAt - want) <= 1.5f) distOk++;
                    dists.Add($"troop {t} {(vehicle ? "light tank" : "squad")} {dAt:0.0} m");
                    if (i == 0) { foreach (var _ in Wait(1.3)) yield return null; Shot("monkeys_troop_0_2_jump_h34"); foreach (var _ in Wait(1.2)) yield return null; Shot("monkeys_troop_0_3_run_h34"); }
                }
                else
                {
                    dists.Add($"troop {t} {(vehicle ? "light tank" : "squad")} NO JUMP");
                    r.Note($"troop {t} (approached by a {(vehicle ? "light tank" : "squad")}{(byPath ? " on a path" : "")}): NO JUMP. {sat0} of its monkeys sat on the limbs when the unit set out; the unit came as near as {nearest:0.0} m to the tree's foot (they jump at {(vehicle ? JungleFauna.I.monkeyVehicle : JungleFauna.I.monkeyRobot):0} m from a moving {(vehicle ? "vehicle" : "robot")})");
                }
                double watch = W.Time + 12;
                while (W.Time < watch) { sample(); yield return null; }
                Remove(id);                                                                   // taken out again, so the place gets quiet
                double off = W.Time + 8;
                while (W.Time < off) { sample(); yield return null; }
            }
            // the last troops climb back too: timed on five
            double wait = W.Time + 120;
            while (W.Time < wait && JungleFauna.ReturnAfter.Count < 5) { sample(); yield return null; }
            r.Note("frights: " + string.Join("; ", dists));
            r.V(asked >= 3 && jumped == asked, $"{jumped} of {asked} approaches made the troop jump down and run (wanted all, at least 3; {free.Count} of {JungleFaunaData.TroopTrees.Length} troops free of the match's own units at the start, {order.Length - asked} of {order.Length} planned approaches left out because units of the match stood near by then)");
            r.V(distOk == asked, $"{distOk} of {asked} with the unit at its fright distance within 1.5 m (robot {JungleFauna.I.monkeyRobot:0} m, vehicle {JungleFauna.I.monkeyVehicle:0} m)");
            r.V(samples > 0 && onWater == 0 && inRock == 0, $"{samples} samples of running monkeys (every 0.2 s): {onWater} on a water tile, {inRock} inside a rock or building tile (wanted 0 and 0)");
            r.V(ends > 0 && endsOk == ends, $"{endsOk} of {ends} runs ended in dense trees or at the map's edge");
            int alarms = Asked("critter_monkey_alarm") - alarm0, lands = Asked("critter_monkey_land") - land0, jumps = Asked("critter_monkey_jump") - jump0;
            r.V(alarms > 0 && lands > 0 && jumps > 0, $"in the sound log: {alarms} alarm calls, {jumps} jumps off the limb, {lands} landings asked for (played: alarm {PlayedN("critter_monkey_alarm")}, jump {PlayedN("critter_monkey_jump")}, landing {PlayedN("critter_monkey_land")}; an event with no file yet is logged and silent)");
            var back = JungleFauna.ReturnAfter.Take(5).ToList();
            r.V(back.Count == 5 && back.All(s => s >= 40f && s <= 71f), $"{back.Count} of 5 timed climbs back after {string.Join(", ", back.Select(s => s.ToString("0")))} quiet seconds (wanted 40 to 70 each)");
            r.End();
        }

        static int Asked(string evt) { L16AnimalKit.Asked.TryGetValue(evt, out int n); return n; }
        static int PlayedN(string evt) { L16AnimalKit.Played.TryGetValue(evt, out int n); return n; }

        // ================================================================== 5 the long watch
        static bool watching; static double watchFrom, nextHalf, nextSecond; static int eagleMax, crocSamples, crocNearSim, snakeSamples, snakeOnRoad, snakeNearBuilding, swimMax, hissesFrom, takeOffsFrom;
        static int lensSamples, lensNear; static float lensLeast;
        static readonly List<Vector3> flyPos = new List<Vector3>(); static readonly List<float> flySpan = new List<float>(); static readonly List<bool> flyDrawn = new List<bool>();
        static double leastSim; static readonly List<Vector4> buildings = new List<Vector4>(); static float watchLog0;

        [MenuItem("OpenZ/L16 Jungle/Checks/Animals 5: long watch, START (Play, level 16; let a match run 10 to 30 game minutes)")]
        static void WatchStart()
        {
            if (!Application.isPlaying || !G || W == null || G.level != "L16") { Debug.LogWarning("[OpenZ J8] long watch: start Play on level 16 first"); return; }
            watching = true; watchFrom = W.Time; nextHalf = nextSecond = W.Time; eagleMax = crocSamples = crocNearSim = snakeSamples = snakeOnRoad = snakeNearBuilding = swimMax = 0; leastSim = 99;
            hissesFrom = JungleFauna.Hisses; takeOffsFrom = JungleFauna.TakeOffs; watchLog0 = Time.unscaledTime;
            lensSamples = lensNear = 0; lensLeast = 99f;
            CrocodileView.LeastDrawnBank = 99f; CrocodileView.BankSamples = CrocodileView.BankNear = 0;
            buildings.Clear();
            foreach (var o in W.Objects) if (o.Kind == ZKind.Building && !o.IsBridge) buildings.Add(new Vector4(o.LocX / 8f, o.LocY / 8f, (o.LocX + o.WidthPix) / 8f, (o.LocY + o.HeightPix) / 8f));
            EditorApplication.update -= WatchTick; EditorApplication.update += WatchTick;
            Debug.Log("[OpenZ J8] long watch started at game time " + W.Time.ToString("0") + " s (speed " + G.gameSpeed + "); menu 'Animals 5: long watch, REPORT' ends it");
        }

        static void WatchTick()
        {
            if (!Application.isPlaying || !watching || W == null) { EditorApplication.update -= WatchTick; return; }
            if (W.Time >= nextHalf)
            {   // every 0.5 s of game time: every animal against the rule's shore and against the drawn bank
                nextHalf += 0.5;
                foreach (var c in W.Crocs)
                {
                    if (c.Hidden) continue;
                    crocSamples++; double f = OzL16CrocCount.OutlineFree(W, c);
                    if (f < leastSim) leastSim = f;
                    if (f < OzL16CrocCount.BankM - 1e-6) crocNearSim++;
                }
                CrocodileView.SampleBanks();
            }
            if (W.Time >= nextSecond)
            {
                nextSecond += 1.0;
                eagleMax = Mathf.Max(eagleMax, Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Count(t => t.name.StartsWith("Eagle") && t.parent && t.parent.name == "AmbientLife"));
                var pos = new List<Vector3>(); var swim = new List<bool>();
                JungleFauna.Snakes(pos, swim);
                swimMax = Mathf.Max(swimMax, swim.Count(s => s));
                for (int k = 0; k < pos.Count; k++)
                {
                    snakeSamples++; var p = pos[k]; float x = p.x, s = -p.z;
                    if (W.Map.ClassAt(Mathf.FloorToInt(x / 2f), Mathf.FloorToInt(s / 2f)) == ZClass.Road) snakeOnRoad++;
                    foreach (var b in buildings) { float dx = Mathf.Max(b.x - x, 0f, x - b.z), dy = Mathf.Max(b.y - s, 0f, s - b.w); if (dx * dx + dy * dy < 36f) { snakeNearBuilding++; break; } }
                }
                // every flying parrot that is drawn, measured from the game camera as it stands now: never nearer than 4 of its wingspans
                var lens = Camera.main ? Camera.main.transform : null;
                JungleFauna.Flying(flyPos, flySpan, flyDrawn);
                for (int k = 0; lens && k < flyPos.Count; k++)
                {
                    if (!flyDrawn[k]) continue;
                    lensSamples++; float spans = Vector3.Distance(lens.position, flyPos[k]) / flySpan[k];
                    if (spans < lensLeast) lensLeast = spans;
                    if (spans < 4f) lensNear++;
                }
            }
        }

        [MenuItem("OpenZ/L16 Jungle/Checks/Animals 5: long watch, REPORT")]
        static void WatchReport()
        {
            if (!watching || W == null) { Debug.LogWarning("[OpenZ J8] long watch: not started"); return; }
            watching = false; EditorApplication.update -= WatchTick;
            var r = new Report("Animals 5 long watch");
            double minutes = (W.Time - watchFrom) / 60.0;
            r.Note($"watched {minutes:0.0} minutes of game time at speed {G.gameSpeed}; hunts {W.CrocHunts}, robots taken {W.CrocKills}, gave up before the rise {W.CrocMissBefore}, missed after it {W.CrocMissAfter}; {JungleFauna.Status()}");
            r.V(crocSamples > 0 && crocNearSim == 0, $"{crocNearSim} of {crocSamples} samples (every 0.5 s) with a part of an animal nearer than {OzL16CrocCount.BankM} m to the rule's shore or on land (wanted 0); least {leastSim:0.00} m");
            r.V(CrocodileView.BankSamples > 0 && CrocodileView.BankNear == 0, $"{CrocodileView.BankNear} of {CrocodileView.BankSamples} samples with a part of a DRAWN animal nearer than 0.6 m to the DRAWN bank (the ground the game shows against the pool's water height; wanted 0); least ring of water round an outline {CrocodileView.LeastDrawnBank:0.0} m");
            int cries = OzSoundLog.Since(watchLog0).Count(e => e.key != null && (e.key.Contains("crow") || e.key.Contains("eagle")));
            r.V(eagleMax == 0 && cries == 0, $"{eagleMax} eagle objects at most, {cries} eagle cries in the sound log (wanted 0 and 0; the log keeps its last {OzSoundLog.Max} lines)");
            int squawkAsked = Asked("amb_l16_parrot_squawk"), squawkPlayed = PlayedN("amb_l16_parrot_squawk");
            r.V(JungleFauna.FlyingParrots == 3 && squawkAsked > 0 && squawkAsked == squawkPlayed, $"{JungleFauna.FlyingParrots} parrots in the air; {squawkAsked} squawks asked for, {squawkPlayed} played and logged (wanted: every one)");
            r.V(lensSamples > 0 && lensNear == 0, $"{lensNear} of {lensSamples} samples (every 1 s) of a drawn flying parrot nearer than 4 of its wingspans to the camera (wanted 0: like the eagle it shrinks away between 6 and 4 wingspans and is not drawn nearer); the nearest drawn one was {lensLeast:0.0} wingspans from the lens");
            r.V(JungleFauna.SnakesTotal == 6 && swimMax <= 2 && JungleFauna.MaxSnakesSwimming <= 2, $"{JungleFauna.SnakesTotal} snakes; most in the water at once {Mathf.Max(swimMax, JungleFauna.MaxSnakesSwimming)} (wanted at most 2; sampled every 1 s)");
            r.V(snakeSamples > 0 && snakeOnRoad == 0 && snakeNearBuilding == 0, $"{snakeSamples} samples of snakes out of their dens: {snakeOnRoad} on a road tile, {snakeNearBuilding} within 6 m of a building (wanted 0 and 0)");
            int hisses = JungleFauna.Hisses - hissesFrom;
            r.Note($"hisses heard while watching: {hisses} (a unit within {JungleFauna.I.snakeAlert} m of a snake that is out; counted by the menu 'Animals 6: a snake hisses at a squad')");
            r.V(minutes >= 10, $"watched {minutes:0.0} game minutes (wanted 10 or more; 30 for the crocodile line)");
            r.End();
        }

        // ================================================================== 6 a snake hisses at a squad
        [MenuItem("OpenZ/L16 Jungle/Checks/Animals 6: a snake hisses at a squad (Play, level 16)")]
        static void HissMenu() => Begin("Animals 6 snake hiss", Hiss());

        static IEnumerator Hiss()
        {
            var r = new Report("Animals 6 a snake hisses at a squad");
            if (G.level != "L16" || !JungleFauna.I) { r.V(false, "level 16 with its animals is not running"); r.End(); yield break; }
            var pos = new List<Vector3>(); var swim = new List<bool>();
            bool out1 = false, hissed = false; int tries = 0, hisses0 = JungleFauna.Hisses; float t0 = Time.unscaledTime, dAt = -1f, waited = 0f;
            while (!hissed && tries < 4)
            {
                tries++;
                // a snake that is out of its den, on its bank (not in the water), with a straight walkable line of 14 m to it. They come out
                // by themselves when no unit is within 14 m of the den: the first 3 s into the level, each again 15 to 45 s after it went in.
                int pick = -1, sx = 0, sy = 0, ex = 0, ey = 0; float w0 = Time.unscaledTime;
                while (Time.unscaledTime - w0 < 90f && pick < 0)
                {
                    JungleFauna.Snakes(pos, swim);
                    for (int k = 0; k < pos.Count && pick < 0; k++)
                        if (!swim[k] && Approach(new Vector3(pos[k].x, 0f, pos[k].z), 14f, false, tries + k, out sx, out sy, out ex, out ey)) pick = k;
                    if (pick < 0) yield return null;
                }
                waited += Time.unscaledTime - w0;
                if (pick < 0) { r.Note($"try {tries}: no snake was out on a bank a squad can walk to within 90 s ({JungleFauna.Status()})"); continue; }
                out1 = true;
                var at = pos[pick];
                OzL16Proof.Cam(at.x, at.z, 22f);
                int id = OzL16Proof.Spawn("grunt", true, sx, sy);
                { int stx = Mathf.FloorToInt(at.x / 2f), sty = Mathf.FloorToInt(-at.z / 2f); if (W.Path.TilePassable(stx, sty, true)) { ex = stx; ey = sty; } }
                Tough(id); OzL16Proof.Move(id, ex, ey);
                int before = JungleFauna.Hisses; double end = W.Time + 40;
                while (W.Time < end && JungleFauna.Hisses == before) yield return null;
                var squad = Obj(id);
                if (JungleFauna.Hisses > before && squad != null)
                {   // how far the nearest robot of the squad is from the nearest snake that is out, on the frame after the hiss
                    JungleFauna.Snakes(pos, swim);
                    foreach (var o in new[] { squad }.Concat(squad.Minions)) foreach (var p in pos) { float d = Flat(PosOf(o), p); if (dAt < 0f || d < dAt) dAt = d; }
                    hissed = dAt >= 0f && dAt <= JungleFauna.I.snakeAlert + 2f;
                    if (!hissed) r.Note($"try {tries}: a hiss was counted, but the squad was {dAt:0.0} m from the nearest snake: another unit's snake; again");
                    else { foreach (var _ in WaitReal(0.5f)) yield return null; Shot("snake_rears_up_at_a_squad_h22"); }
                }
                else r.Note($"try {tries}: the squad reached the place and no snake hissed within 40 s (it had gone home or into the water before the squad came; {JungleFauna.Status()})");
                OzL16Proof.Move(id, sx, sy);                                                  // the test squad goes back where it came from
                foreach (var _ in Wait(4)) yield return null;
            }
            if (!hissed)
            {   // no approach made a snake rear up: a squad set down right beside one that is out (no walk)
                JungleFauna.Snakes(pos, swim); int before2 = JungleFauna.Hisses; bool set = false; var beside = Vector3.zero;
                for (int k = 0; k < pos.Count && !set; k++)
                {
                    if (swim[k]) continue;
                    int ctx = Mathf.FloorToInt(pos[k].x / 2f), cty = Mathf.FloorToInt(-pos[k].z / 2f);
                    for (int rr = 0; rr <= 2 && !set; rr++) for (int dy = -rr; dy <= rr && !set; dy++) for (int dx = -rr; dx <= rr && !set; dx++)
                    {
                        int tx = ctx + dx, ty = cty + dy;
                        if (!W.Path.TilePassable(tx, ty, true) || Flat(new Vector3(tx * 2f + 1f, 0f, -(ty * 2f + 1f)), pos[k]) > JungleFauna.I.snakeAlert - 1.5f) continue;
                        OzL16Proof.Cam(pos[k].x, pos[k].z, 22f); int id3 = OzL16Proof.Spawn("grunt", true, tx, ty); Tough(id3); set = true; beside = pos[k];
                    }
                }
                bool direct = false;
                if (set) { double end3 = W.Time + 8; while (W.Time < end3 && JungleFauna.Hisses == before2) yield return null; direct = JungleFauna.Hisses > before2; }
                if (direct) { foreach (var _ in WaitReal(0.5f)) yield return null; Shot("snake_rears_up_at_a_squad_set_beside_it_h22"); }
                r.Note(set ? $"no approach made a snake rear up; a squad set down beside the snake at {beside.x:0.0}, {beside.z:0.0}: {(direct ? "it reared up and hissed" : "NO hiss in 8 s")} ({JungleFauna.Status()})" : "no approach made a snake rear up, and no snake was out to set a squad beside");
            }
            r.V(out1, $"a snake was out of its den on a bank a squad can walk to (waited {waited:0} s in all)");
            r.V(hissed, $"a squad walked up to it and it reared up and hissed: hisses counted {hisses0} -> {JungleFauna.Hisses}, the nearest robot {dAt:0.0} m from the snake on the next frame (it rears up within {JungleFauna.I.snakeAlert} m; {tries} tr{(tries == 1 ? "y" : "ies")})");
            var log = OzSoundLog.Since(t0).Where(e => e.src == "L16" && e.key == "amb_l16_snake_hiss").ToList();
            int played = log.Count(e => e.outcome.StartsWith("PLAY"));
            r.V(played >= 1, $"amb_l16_snake_hiss is a PLAY line of the sound log ({played} played of {log.Count} asked for since the check began{(log.Count > played ? "; the others: " + string.Join(", ", log.Where(e => !e.outcome.StartsWith("PLAY")).Select(e => e.outcome).Distinct()) : "")})");
            r.End();
        }
    }
}
