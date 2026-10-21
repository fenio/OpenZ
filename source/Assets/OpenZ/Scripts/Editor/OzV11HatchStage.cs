using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenZ.Sim;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Staging helper for pictures of the hatch drivers who fire their own weapon (rule: Sim/ZObject.DriverFire.cs, view:
    /// View/BoardingView.DriverFire.cs). Editor only, Play mode, in a running level. Menu: OpenZ/V1.1/Stage Hatch Drivers For
    /// Pictures (b11, Play mode), or OpenZ.EditorTools.OzV11HatchStage.Run().
    ///
    /// Three scenes, one after the other, on open ground the helper looks for itself (plain ground or road, clear of every tree,
    /// rock, hut and building, as far from forts, guns and units as the level allows):
    ///  a  a light tank whose driver is a Laser
    ///  b  a light tank with the default driver, a grunt
    ///  c  a troop carrier with its driver only (a grunt, nobody else aboard)
    /// Each vehicle is made by the rules the way the counted check of this rule makes it (ZWorld.CreateObject, then the driver's
    /// type), belongs to the player's team and stands still. 100 px east or west of it stand three enemy grunts in a V; they cannot move,
    /// shoot or die, so the scene holds. A tank's lid opens 4 times in 5 on a new target: when it stays shut the grunts are
    /// replaced (a new target, a new roll), up to 6 times. The helper waits until the driver is up and has fired twice, then
    /// takes the picture a moment after his next shot, while that shot is in the air, from the normal game camera (height 70,
    /// looking at the place), and removes the staged units without a death (the idiom of the gate battery).
    ///
    /// It writes on_screen.txt (per scene: driver type, hatch open, shots counted, projectile, the sound log lines of the shots)
    /// subfolder b11_driver_fire). It changes no rule and no setting: the game speed, the time scale and the saved settings are
    /// not touched, the Game view keeps its size, and the camera goes back to where it was.
    /// </summary>
    public static class OzV11HatchStage
    {
        const string Tag = "[OpenZ hatch drivers staged]";
        const string RunKey = "OpenZ.V11.HatchStage.startedAt";                                               // SessionState: a run is going (it survives a script reload, a static field does not)
        const float MaxSeconds = 300f, CamHeight = 70f;
        const int Big = 1000000000, EnemyPx = 100;
        public static string Report = "not run";
        /// <summary></summary>
        public static string Folder;
        /// <summary>The top-left tile the staged vehicle stands on. Under 0 (default) = the helper looks for open ground itself.
        /// TileSide: with a tile given by hand, the enemy stands east (1) or west (-1) of the vehicle.</summary>
        public static int TileX = -1, TileY = -1, TileSide = 1;

        static readonly (string key, string vehicle, string driver, string file)[] Scenes =
        {
            ("a", "light", "laser", "b11_a_tank_laser_driver"), ("b", "light", "grunt", "b11_b_tank_grunt_driver"), ("c", "apc", "grunt", "b11_c_carrier_lone_driver"),
        };

        static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        static readonly List<ZObject> staged = new List<ZObject>();
        static int waitFrame, good;
        static float waitUntil, height0, carrierFireAt;
        static Vector3 focus0;
        static bool camKept;
        static int carrierShots;
        static string dir;

        struct Place { public int x, y, clear, south, side; public float room; }                             // side: the enemy stands east (1) or west (-1) of the vehicle

        [MenuItem("OpenZ/V1.1/Stage Hatch Drivers For Pictures (b11, Play mode)")]
        public static void Run()
        {
            if (!EditorApplication.isPlaying) { Debug.LogWarning(Tag + " enter Play mode first (a running level)"); return; }
            float started = SessionState.GetFloat(RunKey, -1f);
            if (stack.Count > 0 || (started >= 0f && EditorApplication.timeSinceStartup - started < MaxSeconds)) { Debug.LogWarning(Tag + " a run is still going"); return; }
            var g = ZGame.I; var rts = Object.FindFirstObjectByType<RtsCamera>();
            string no = !g || g.World == null ? "no level is running" : !rts ? "no game camera" : g.paused ? "the game is paused: close the menu first" : g.World.GameOver ? "the match is over"
                      : !ZWorld.DriverFireDeviation ? "the rule is switched off (Zod exact): there is nothing to stage" : null;
            if (no != null) { Report = Tag + " NOT RUN: " + no; Debug.LogError(Report); return; }
            SessionState.SetFloat(RunKey, (float)EditorApplication.timeSinceStartup);
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) EditorWindow.FocusWindowIfItsOpen(gv);
            dir = OutDir(); Directory.CreateDirectory(Path.Combine(dir, "for_owner"));
            good = 0; staged.Clear(); Report = "running";
            stack.Push(Stage(g, rts));
            waitFrame = 0; waitUntil = 0f; EditorApplication.update += Step;
        }

        static string OutDir()
        {
            if (!string.IsNullOrEmpty(Folder)) return Path.GetFullPath(Folder);
            string all = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones")), stage = "v1_1";
            if (Directory.Exists(all))
            {
                var ds = Directory.GetDirectories(all, "v1_1*");
                System.Array.Sort(ds, System.StringComparer.Ordinal);
                if (ds.Length > 0) stage = Path.GetFileName(ds[ds.Length - 1]);
            }
            return Path.Combine(all, stage, "b11_driver_fire");
        }

        // a step yields an int (frames), a float (seconds of real time) or another step (run to its end first)
        static void Step()
        {
            if (stack.Count == 0 || !EditorApplication.isPlaying) { End(); return; }
            if (Time.frameCount < waitFrame || Time.realtimeSinceStartup < waitUntil) return;
            var top = stack.Peek(); bool more;
            try { more = top.MoveNext(); }
            catch (System.Exception ex) { Debug.LogException(ex); Report = Tag + " STOPPED: exception " + ex.Message; Debug.LogError(Report); End(); return; }
            if (!more) { stack.Pop(); if (stack.Count == 0) End(); return; }
            if (top.Current is IEnumerator child) stack.Push(child);
            else if (top.Current is float s) waitUntil = Time.realtimeSinceStartup + s;
            else waitFrame = Time.frameCount + (top.Current is int n ? n : 1);
        }

        /// <summary>Every way out: nothing staged stays in the level, the camera is back, the next start is free.</summary>
        static void End()
        {
            stack.Clear(); EditorApplication.update -= Step; SessionState.EraseFloat(RunKey);
            var g = Application.isPlaying ? ZGame.I : null;
            if (g && g.World != null) foreach (var o in staged) Remove(g, o);
            staged.Clear();
            var rts = Application.isPlaying ? Object.FindFirstObjectByType<RtsCamera>() : null;
            if (camKept && rts) { rts.focus = focus0; rts.SetHeightImmediate(height0); }
            camKept = false;
        }

        // ------------------------------------------------------------------ staged units
        /// <summary>Out of the level without a death, a wreck or a sound: the rules drop it at their next tick, its view goes now.</summary>
        static void Remove(ZGame g, ZObject o)
        {
            if (o == null) return;
            if (o.KillAt < 0) o.KillAt = g.World.Time;
            if (g.Views.TryGetValue(o.Id, out var v)) { g.Views.Remove(o.Id); if (v) Object.Destroy(v.gameObject); }
        }

        /// <summary>Three enemy grunts in a V around (cx, cy), the point towards the vehicle (side: they stand east, 1, or west, -1,
        /// of it). Each is a squad of one (a squad is made on one spot and only spreads when it walks). They cannot move, shoot or die.</summary>
        static List<ZObject> Enemies(ZGame g, Team foe, int cx, int cy, int side)
        {
            var l = new List<ZObject>();
            foreach (var (dx, dy) in new[] { (-8, 0), (8, -14), (8, 14) })
            {
                var e = g.World.CreateRobotGroup("grunt", cx + dx * side - 8, cy + dy - 8, foe, 1);
                e.MaxHealth = e.Health = Big; e.Damage = 0; e.MoveSpeed = 0; e.RealMoveSpeed = 0;
                g.SpawnUnitView(e); l.Add(e); staged.Add(e);
            }
            return l;
        }

        static bool Up(ZObject v) => v.IsApc ? v.ApcDriverUp : v.LidOpen && v.DriverFireUp;
        static bool FigureOut(ZObject v) => v.IsApc ? BoardingView.TryCrewMuzzle(v.Id, out _) : BoardingView.DriverMuzzleNow(v.Id, out _);
        /// <summary>The driver's shots: a tank's are counted by the rules (ZObject.DriverShots); the carrier's shots are its own shot
        /// events, counted here as its view shows them (UnitView.LastFireAt moves with every shot drawn).</summary>
        static int Shots(ZObject v) => v.IsApc ? carrierShots : v.DriverShots;

        static string Weapon(string type) =>
            type == "laser" ? "the Laser's own green bolts, two to a shot, as a Laser fires on foot"
            : type == "grunt" ? "the grunt's bolt, as the crane driver fires it"
            : "the weapon of a " + type + " on foot";

        // ------------------------------------------------------------------ the place
        /// <summary>How far out of reach the scene is (px): for every fort, gun, robot and vehicle of the level, its distance to the
        /// staged vehicle or to the grunts (whichever is nearer) minus its own range and the distance at which a unit takes
        /// notice (the rule table's); the smallest of these, and whose it is. Under 0 = that one could fire into the scene.</summary>
        static float Room(ZWorld W, int vx, int vy, int side, out string what)
        {
            float best = float.MaxValue; what = "nothing armed on the level";
            int fortRange = ZObject.FortGunRange(W.S.Unit("howitzer")); float notice = (float)W.S.agro_distance;
            foreach (var o in W.Objects)
            {
                if (o.KillAt >= 0 || staged.Contains(o)) continue;
                bool fort = o.Kind == ZKind.Building && o.Type.StartsWith("fort");
                if (!fort && !o.IsUnit) continue;
                float ax = o.CenterX - vx, ay = o.CenterY - vy, bx = ax - side * EnemyPx, d = Mathf.Sqrt(Mathf.Min(ax * ax + ay * ay, bx * bx + ay * ay));
                float room = d - (fort ? fortRange : o.AttackRadius) - notice;
                if (room < best) { best = room; what = $"{(o.Owner == Team.Neutral ? "" : o.Owner.ToString().ToLowerInvariant() + " ")}{o.Type} {d / 16f:0.0} tiles away, range {(fort ? fortRange : o.AttackRadius) / 16f:0.0} tiles"; }
            }
            return best;
        }

        static readonly ZObject probe = new ZObject { Kind = ZKind.Vehicle, WidthPix = 32, HeightPix = 32 };
        /// <summary>A vehicle on this tile, or one tile off it, would stand on a bridge or in the door lane of a fort or factory,
        /// where no driver comes up (the rules' own position test, asked with a vehicle that is not in the level).</summary>
        static bool NoDriverUpHere(ZWorld W, int tx, int ty)
        {
            foreach (string type in new[] { "light", "apc" })
                foreach (var (dx, dy) in new[] { (0, 0), (16, 0), (-16, 0), (0, 16), (0, -16) })
                {
                    probe.Type = type; probe.SetCords(tx * 16 + dx, ty * 16 + dy);
                    if (W.SnipeSafeSpot(probe)) return true;
                }
            return false;
        }

        /// <summary>Open places for one scene, the best first. The vehicle stands on tiles x..x+1, y..y+1, the grunts 6 tiles east
        /// or west of it: the box x-2..x+9 (x-8..x+3 with the grunts west), y-2..y+3 must be plain ground or road that robots and vehicles may stand on, with nothing of the level
        /// within `clear` tiles of it (`south` tiles on the side the camera looks from, where a tree would stand in the picture),
        /// and no bridge or door lane under the vehicle. The widest clearance that still finds a place out of reach of every fort,
        /// gun and unit is taken; with none out of reach, the place with the most room.</summary>
        static List<Place> Places(ZWorld W)
        {
            int w = W.Map.width, h = W.Map.height;
            List<Place> any = null;
            foreach (var (clear, south) in new[] { (3, 5), (3, 4), (2, 4), (2, 3), (2, 2) })
            {
                var taken = new bool[w, h];
                foreach (var o in W.Objects)
                {
                    if (o.KillAt >= 0) continue;
                    int x0 = o.LocX >> 4, y0 = o.LocY >> 4, x1 = (o.LocX + Mathf.Max(16, o.WidthPix) - 1) >> 4, y1 = (o.LocY + Mathf.Max(16, o.HeightPix) - 1) >> 4;
                    for (int y = y0 - south; y <= y1 + clear; y++)
                        for (int x = x0 - clear; x <= x1 + clear; x++)
                            if (x >= 0 && y >= 0 && x < w && y < h) taken[x, y] = true;
                }
                var list = new List<Place>();
                for (int cy = 4; cy < h - 6; cy++)
                    for (int cx = 10; cx < w - 12; cx++)
                        for (int side = 1; side >= -1; side -= 2)
                        {
                            bool open = true; int xa = side > 0 ? cx - 2 : cx - 8;
                            for (int y = cy - 2; y <= cy + 3 && open; y++)
                                for (int x = xa; x <= xa + 11 && open; x++)
                                {
                                    var c = W.Map.ClassAt(x, y);
                                    open = (c == ZClass.Ground || c == ZClass.Road) && !taken[x, y] && W.Path.TilePassable(x, y, false) && W.Path.TilePassable(x, y, true);
                                }
                            if (open && !NoDriverUpHere(W, cx, cy)) list.Add(new Place { x = cx, y = cy, clear = clear, south = south, side = side, room = Room(W, cx * 16 + 16, cy * 16 + 16, side, out _) });
                        }
                list.Sort((a, b) => b.room.CompareTo(a.room));
                if (list.Count > 0 && (any == null || list[0].room > any[0].room)) any = list;
                if (list.Count > 0 && list[0].room >= 0f) break;
            }
            return any ?? new List<Place>();
        }

        /// <summary>One place per scene: the best, and further ones only when they are also out of reach of every fort, gun and
        /// unit and at least 12 tiles from the places before them. A level with one such place has all three scenes on it.</summary>
        static List<Place> Pick(List<Place> all)
        {
            var l = new List<Place>();
            foreach (var p in all)
            {
                if (l.Count > 0 && p.room < 0f) break;
                bool apart = true; foreach (var q in l) if ((p.x - q.x) * (p.x - q.x) + (p.y - q.y) * (p.y - q.y) < 144) apart = false;
                if (apart) l.Add(p);
                if (l.Count == Scenes.Length) break;
            }
            return l;
        }

        // ------------------------------------------------------------------ the run
        static IEnumerator Stage(ZGame g, RtsCamera rts)
        {
            var W = g.World; var hud = OpenZHud.I;
            Team me = hud ? hud.Player : Team.Red, foe = me == Team.Red ? Team.Blue : Team.Red;
            var sb = new StringBuilder();
            sb.AppendLine($"Hatch drivers on screen: level {g.level}, Game view {Screen.width}x{Screen.height}, game speed {g.gameSpeed:0.##}, the vehicles belong to {me.ToString().ToLowerInvariant()}, the enemy grunts to {foe.ToString().ToLowerInvariant()}.");
            List<Place> places;
            if (TileX >= 0 && TileY >= 0)
            {
                int side = TileSide < 0 ? -1 : 1;
                places = new List<Place> { new Place { x = TileX, y = TileY, side = side, room = Room(W, TileX * 16 + 16, TileY * 16 + 16, side, out _) } };
                sb.AppendLine($"Place: tile {TileX},{TileY}, given by hand (OzV11HatchStage.TileX / TileY / TileSide); all three scenes stand there.");
            }
            else
            {
                places = Pick(Places(W));
                if (places.Count == 0) { Report = Tag + " NOT RUN: no open ground of 12 x 6 tiles found on this level; give a tile by hand (OzV11HatchStage.TileX / TileY)"; Debug.LogError(Report); yield break; }
                sb.AppendLine($"Place: found by the helper. Plain ground or road, nothing of the level within {places[0].clear} tiles of the staging box ({places[0].south} tiles on the camera's side), no bridge and no door lane; "
                              + (places.Count == 1 ? "one such place on this level is out of reach of every fort, gun and unit (or none is, and this one has the most room), so all three scenes stand on it." : places.Count + " such places out of reach of every fort, gun and unit, one per scene."));
            }
            focus0 = rts.focus; height0 = rts.height; camKept = true;
            for (int i = 0; i < Scenes.Length; i++)
            {
                yield return One(g, rts, me, foe, Scenes[i], places[Mathf.Min(i, places.Count - 1)], sb);
                yield return 1.5f;                                                                          // the last rounds and their smoke are gone before the next scene
            }
            Report = $"{Tag} {(good == Scenes.Length ? "DONE" : "INCOMPLETE")} {good}/{Scenes.Length} scenes with the driver up and firing in the picture (level {g.level}, {Screen.width}x{Screen.height}); text {Path.Combine(dir, "on_screen.txt").Replace('\\', '/')}, pictures in {Path.Combine(dir, "for_owner").Replace('\\', '/')}";
            sb.AppendLine(Report);
            try { File.WriteAllText(Path.Combine(dir, "on_screen.txt"), sb.ToString()); }
            catch (System.Exception ex) { Debug.LogWarning(Tag + " could not write on_screen.txt: " + ex.Message); }
            if (good == Scenes.Length) Debug.Log(Report); else Debug.LogError(Report);
        }

        static IEnumerator One(ZGame g, RtsCamera rts, Team me, Team foe, (string key, string vehicle, string driver, string file) sc, Place p, StringBuilder sb)
        {
            var W = g.World; float t0 = Time.unscaledTime;
            float speed = Mathf.Clamp(g.gameSpeed, 0.25f, 4f);
            int onFoot0 = BoardingView.DriverShotsOnFoot, fallback0 = BoardingView.DriverShotsFallback, noFigure0 = BoardingView.DriverShotsNoFigure;

            // the vehicle, as the counted check of the rule makes it: by the rules, then the driver's type; it stands still
            var v = W.CreateObject("vehicle", sc.vehicle, p.x * 16, p.y * 16, me);
            v.DriverType = sc.driver; if (v.Drivers.Count > 0) v.Drivers[0] = W.S.Unit(sc.driver).MaxHealth;
            v.MoveSpeed = 0; v.RealMoveSpeed = 0;
            v.ResetDamageInfo();
            staged.Add(v);
            var view = g.SpawnUnitView(v);
            int ex = v.CenterX + p.side * EnemyPx, ey = v.CenterY;
            var mid = ZGame.PxToWorld(v.CenterX + p.side * EnemyPx * 0.5, v.CenterY); mid.y = 0f;
            carrierShots = 0; carrierFireAt = view ? view.LastFireAt : -100f;
            void Tick()
            {   // every frame of a wait: the camera stays on the place (a pointer on a screen edge would scroll it away); the carrier's shots are counted
                rts.focus = mid; if (Mathf.Abs(rts.height - CamHeight) > 0.01f) rts.SetHeightImmediate(CamHeight);
                if (view && view.LastFireAt != carrierFireAt) { carrierFireAt = view.LastFireAt; carrierShots++; }
            }
            rts.focus = mid; rts.SetHeightImmediate(CamHeight);

            // an enemy in range; a tank's lid opens 4 times in 5 on a new target, so a shut lid gets a new target
            List<ZObject> enemies = null; int targets = 0; bool up = false, snipeSafe = W.SnipeSafeSpot(v);
            while (targets < 6 && !up)
            {
                if (enemies != null) { foreach (var e in enemies) Remove(g, e); yield return 0.3f; }
                enemies = Enemies(g, foe, ex, ey, p.side); targets++;
                double simUntil = W.Time + 2.5; float give = Time.realtimeSinceStartup + 12f;
                while (!(up = Up(v)) && W.Time < simUntil && Time.realtimeSinceStartup < give) { Tick(); yield return 1; }
            }

            // up and two shots fired; then the next shot, and the picture a moment after it, while it is in the air
            float until = Time.realtimeSinceStartup + 25f;
            while (up && Shots(v) < 2 && Time.realtimeSinceStartup < until) { Tick(); yield return 1; }
            int before = Shots(v); until = Time.realtimeSinceStartup + 6f;
            while (up && Shots(v) == before && Time.realtimeSinceStartup < until) { Tick(); yield return 1; }
            bool timed = up && Shots(v) > before;
            if (timed) { until = Time.realtimeSinceStartup + 0.09f / speed; while (Time.realtimeSinceStartup < until) { Tick(); yield return 1; } }
            bool open = Up(v), figure = FigureOut(v); int shots = Shots(v), hits = v.DriverHits, near = 0;
            foreach (var o in W.Objects)
                if (o.IsUnit && o.Owner != Team.Neutral && o.KillAt < 0 && !staged.Contains(o) && (o.CenterX - v.CenterX) * (o.CenterX - v.CenterX) + (o.CenterY - v.CenterY) * (o.CenterY - v.CenterY) < 320 * 320) near++;
            string path = Path.Combine(dir, "for_owner", sc.file + ".png");
            try { if (File.Exists(path)) File.Delete(path); } catch (System.Exception) { }
            ScreenCapture.CaptureScreenshot(path);
            until = Time.realtimeSinceStartup + 4f;
            while (!File.Exists(path) && Time.realtimeSinceStartup < until) { Tick(); yield return 1; }
            bool written = File.Exists(path);
            until = Time.realtimeSinceStartup + 0.6f; while (Time.realtimeSinceStartup < until) { Tick(); yield return 1; }   // a few more shots for the sound log

            // what was seen and heard
            var ears = Object.FindFirstObjectByType<AudioListener>();
            float fromEars = ears ? Vector3.Distance(ears.transform.position, g.CenterWorld(v)) : -1f;
            var heard = OzSoundLog.Since(t0).FindAll(e => e.key != null && (e.key.StartsWith(sc.driver == "laser" ? "laser" : "bolt") || e.key.Contains("hatch") || e.key.Contains("lid"))
                                                          && (e.dist < 0f || fromEars < 0f || Mathf.Abs(e.dist - fromEars) <= 25f));
            int played = heard.FindAll(e => e.outcome != null && e.outcome.StartsWith("PLAY") && !e.key.Contains("hatch") && !e.key.Contains("lid")).Count;
            bool ok = open && figure && shots >= 2 && written;
            if (ok) good++;
            var u = W.S.Unit(sc.driver);
            sb.AppendLine();
            sb.AppendLine($"Scene {sc.key}: {(v.IsApc ? "troop carrier, its driver only" : sc.vehicle + " tank")}, driver type {sc.driver} ({v.Drivers.Count} driver, {v.Passengers.Count} passengers aboard). {(ok ? "OK" : "NOT AS WANTED")}");
            float room = Room(W, v.CenterX, v.CenterY, p.side, out string what);
            sb.AppendLine($"  place: tile {p.x},{p.y}, the enemy {EnemyPx} px {(p.side > 0 ? "east" : "west")} of the vehicle; the fort, gun or unit nearest to reaching the scene: {what} ({(room >= 0f ? "out of reach by " + (room / 16f).ToString("0.0") + " tiles" : "IT CAN REACH THE SCENE")}); other armed units within 20 tiles at the picture: {near}"
                          + (snipeSafe ? "; THIS SPOT IS A BRIDGE OR A DOOR LANE: no driver comes up here" : ""));
            sb.AppendLine($"  hatch open: {(open ? "yes" : "NO")} (rules: {(v.IsApc ? "the carrier's driver is up" : "the lid is open and the driver may fire")}); the figure is out in the hatch on screen: {(figure ? "yes" : "NO")}; targets it took until he came up: {targets}");
            sb.AppendLine(v.IsApc
                ? $"  shots counted: {shots} (the carrier's own shots as its view drew them; the rule: its lone driver's rifle, {u.Damage * ZWorld.CrewRifleMul} points a hit, every {u.attack_speed:0.00} s, {u.attack_radius} px)"
                : $"  shots counted: {shots}, of them hits {hits} (the rules' own count; his weapon: {v.DriverGunDamage} points a hit, every {v.DriverGunCycle:0.00} s, {v.DriverGunRadius} px)");
            sb.AppendLine($"  projectile: {Weapon(sc.driver)}" + (v.IsApc ? "" : $"; drawn by the on-foot weapon code {BoardingView.DriverShotsOnFoot - onFoot0} times, by the simple fallback {BoardingView.DriverShotsFallback - fallback0}, without a figure in the hatch {BoardingView.DriverShotsNoFigure - noFigure0}"));
            sb.AppendLine($"  picture: {path.Replace('\\', '/')} ({(written ? "written" : "NOT WRITTEN")}; {(timed ? "taken " + (0.09f / speed).ToString("0.00") + " s after a shot, while it was in the air" : "no further shot came in 6 s: taken as the scene stood")})");
            sb.AppendLine($"  sound log, the shots and the hatch ({heard.Count} lines, {played} of them shot sounds that played; kept: the lines about as far from the listener as the vehicle, {fromEars:0} m, 25 m either way; time, source, key, clip, outcome, volume, distance to the listener):");
            foreach (var line in OzSoundLog.Format(heard).Split('\n')) if (line.Trim().Length > 0) sb.Append("    ").AppendLine(line.TrimEnd());

            foreach (var e in enemies) Remove(g, e);
            Remove(g, v);
        }
    }
}
