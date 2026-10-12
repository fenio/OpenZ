#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// the deck of the (51,58) bridge (Zod: the deck tiles are road palette tiles, walk speed 1.689, zmap.cpp:598-615). Per zone it sums the sim
    /// distance over sim time (px/s) and over real time (m/s), the drawn body's distance over real time (m/s), and what drives the look:
    /// the robot's walk clip rate (Animator.speed, UnitView.strideSpeed) or the vehicle's wheel / track travel rate (VehicleMotion.travel).
    /// Red test units (unkillable, unarmed, the CPU bots cleared as FortDefenderShow does), each on an empty stretch, one after the other; the
    /// camera follows each at the start height (70 m); a frame per unit on each zone in milestones/H0-parity/, taken when the unit passes the
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class RoadSpeedCheck : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Only, Label = "";
        /// <summary>only = comma list of task keys (grunt_road, jeep_road, heavy_road, grunt_sand, jeep_sand, heavy_sand) or null for all;
        /// </summary>
        public static void Run(string only = null, string label = "") { Only = only; Label = label; Report = "running"; new GameObject("RoadSpeedCheck").AddComponent<RoadSpeedCheck>(); }

        /// <summary>
        /// ~60-90 s after entering Play): PlayerPrefs oz_gp53_auto = "tasks|label" starts the check as soon as the level is up, once
        /// (the key is cleared), and the check leaves Play when it is done (editor only). Absent key = nothing happens.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            string job = PlayerPrefs.GetString("oz_gp53_auto", "");
            if (string.IsNullOrEmpty(job)) return;
            PlayerPrefs.DeleteKey("oz_gp53_auto"); PlayerPrefs.Save();
            if (GameBoot.TestMode) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60; }   // H0 round 3: GPU-light test mode for a timing run, at the 60 fps cap
            var parts = job.Split('|');
            AutoStop = true;
            Run(parts[0].Length > 0 && parts[0] != "all" ? parts[0] : null, parts.Length > 1 ? parts[1] : "");
        }
        static bool AutoStop;
        static string Records => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records"));

        class Acc { public double simPx, simT, realT, drawnM, animSum, travelM; public int frames; public double coreSimPx, coreSimT, coreRealT; public int coreFrames; }
        class Task { public string cat, type; public int sx, sy; public (int x, int y)[] route; public (int x, int y) park; public string name; public string Key => name + ((sx >> 4) >= 52 ? "_road" : "_sand"); }

        ZGame g; ZWorld W; RtsCamera cam; DayNight dn; string dir; readonly StringBuilder sb = new StringBuilder(); int frames;
        ZObject bridge; PlayerInput input; readonly StringBuilder shotLog = new StringBuilder(); readonly List<string> coreRows = new List<string>();

        string ZoneAt(int x, int y)
        {
            var c = W.Map.ClassAt(x >> 4, y >> 4);
            if (c == ZClass.Road && bridge != null && x >= bridge.LocX && x < bridge.LocX + bridge.WidthPix && y >= bridge.LocY && y < bridge.LocY + bridge.HeightPix) return "bridge deck";
            return c == ZClass.Road ? "road" : c == ZClass.Ground ? "sand" : c.ToString().ToLower();
        }

        /// <summary>H0 round 3 (reviewer: the road rows read 0.2-0.3 % under Zod, cause not traced): every zone stretch along the straight
        /// route (px from the start centre); frames 2 tiles or more inside both ends of their stretch are the "core" of the zone.</summary>
        List<(string z, float a, float b)> Stretches(int sx, int sy, int ex, int ey)
        {
            var res = new List<(string, float, float)>();
            float len = Mathf.Sqrt((ex - sx) * (ex - sx) + (ey - sy) * (ey - sy)); if (len < 1f) return res;
            string cur = null; float a0 = 0f;
            for (float d = 0f; ; d += 2f)
            {
                bool end = d > len; float q = Mathf.Min(d, len);
                string z = end ? null : ZoneAt(Mathf.RoundToInt(sx + (ex - sx) * q / len), Mathf.RoundToInt(sy + (ey - sy) * q / len));
                if (z != cur) { if (cur != null) res.Add((cur, a0, q)); cur = z; a0 = q; }
                if (end) break;
            }
            return res;
        }
        const float CoreMargin = 32f;   // 2 tiles
        readonly StringBuilder edgeLog = new StringBuilder();

        /// <summary>H0 round 2: per zone, the middle (px along the straight route, from the start centre) of its longest stretch.</summary>
        Dictionary<string, (float mid, float a, float b)> ZoneMids(int sx, int sy, int ex, int ey)
        {
            var res = new Dictionary<string, (float mid, float a, float b)>();
            float len = Mathf.Sqrt((ex - sx) * (ex - sx) + (ey - sy) * (ey - sy)); if (len < 1f) return res;
            string cur = null; float a0 = 0f;
            for (float d = 0f; ; d += 2f)
            {
                bool end = d > len; float q = Mathf.Min(d, len);
                string z = end ? null : ZoneAt(Mathf.RoundToInt(sx + (ex - sx) * q / len), Mathf.RoundToInt(sy + (ey - sy) * q / len));
                if (z != cur)
                {
                    if (cur != null && (!res.TryGetValue(cur, out var old) || q - a0 > old.b - old.a)) res[cur] = ((a0 + q) * 0.5f, a0, q);
                    cur = z; a0 = q;
                }
                if (end) break;
            }
            return res;
        }
        static readonly FieldInfo TravelF = typeof(VehicleMotion).GetField("travel", BindingFlags.NonPublic | BindingFlags.Instance);

        string ZoneOf(ZObject u)
        {
            var c = W.Map.ClassAt(u.CenterX >> 4, u.CenterY >> 4);
            if (c == ZClass.Road && bridge != null && u.CenterX >= bridge.LocX && u.CenterX < bridge.LocX + bridge.WidthPix && u.CenterY >= bridge.LocY && u.CenterY < bridge.LocY + bridge.HeightPix) return "bridge deck";
            return c == ZClass.Road ? "road" : c == ZClass.Ground ? "sand" : c.ToString().ToLower();
        }

        IEnumerator Start()
        {
            g = ZGame.I; W = g?.World;
            if (W == null || g.level != "L02") { Report = "needs Play on L02"; Destroy(gameObject); yield break; }
            float wait0 = Time.realtimeSinceStartup;                                          // auto start: past Zod's level briefing first
            while ((ZodBriefing.BriefingActive || g.paused || W.Time < 2) && Time.realtimeSinceStartup - wait0 < 45f) yield return null;
            W.Bots.Clear();                                                                 // test run only: the CPU does not interfere
            cam = FindFirstObjectByType<RtsCamera>(); dn = FindFirstObjectByType<DayNight>();
            if (dn) { dn.running = false; dn.timeOfDay = 0.08f; dn.Apply(0.08f); }
            dir = Records + "/milestones/H0-parity"; System.IO.Directory.CreateDirectory(dir);
            bridge = W.Objects.FirstOrDefault(o => o.Type == "bridge_vert" && o.LocX == 51 * 16 && o.LocY == 58 * 16);
            sb.AppendLine($"# GP53 road and bridge speeds in Play ({System.DateTime.Now:yyyy-MM-dd HH:mm}, L02, full look)\n");
            sb.AppendLine($"Game speed {g.gameSpeed}, Application.targetFrameRate {Application.targetFrameRate}, vSync {QualitySettings.vSyncCount}, screen {Screen.width}x{Screen.height}, GPU {SystemInfo.graphicsDeviceName}. " +
                          $"Bridge {(bridge == null ? "NOT FOUND" : $"#{bridge.Id} {bridge.Type} tiles ({bridge.LocX >> 4},{bridge.LocY >> 4}) {bridge.WidthPix >> 4}x{bridge.HeightPix >> 4}, deck tiles = class Road (Zod tileinfo is_road, 0 mismatches over both maps)")}. " +
                          "Zod: speed = move_speed x GetTileWalkSpeed (road 1.689, zmap.cpp:598-615; zobject.cpp speed offset). Sums over frames whose start and end tile are in the zone while the unit moves.\n");
            sb.AppendLine("| unit | zone | frames | sim px/s | Zod px/s | sim / Zod | real-clock m/s | drawn m/s | look driver | look / drawn |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            // routes (tiles): road + deck = the N-S road over the (51,58) bridge, cols 52-53, rows 47 -> 64; sand = rows 55-57, x 40 -> 50: open
            // sand with sand on every side (round 1 ran the grunt on row 53, right beside the water branch on row 52, and it read as wading)
            var tasks = new List<Task>
            {
                new Task { name = "grunt", cat = "robot", type = "grunt", sx = 52 * 16 + 8, sy = 47 * 16 + 8, route = new[] { (52 * 16 + 8, 64 * 16 + 8) }, park = (56 * 16 + 8, 65 * 16 + 8) },
                new Task { name = "jeep", cat = "vehicle", type = "jeep", sx = 53 * 16, sy = 48 * 16, route = new[] { (53 * 16, 65 * 16) }, park = (59 * 16, 65 * 16) },
                new Task { name = "heavy", cat = "vehicle", type = "heavy", sx = 53 * 16, sy = 48 * 16, route = new[] { (53 * 16, 65 * 16) }, park = (62 * 16, 65 * 16) },
                new Task { name = "grunt", cat = "robot", type = "grunt", sx = 40 * 16 + 8, sy = 56 * 16 + 8, route = new[] { (50 * 16 + 8, 56 * 16 + 8) }, park = (45 * 16 + 8, 48 * 16 + 8) },
                new Task { name = "jeep", cat = "vehicle", type = "jeep", sx = 41 * 16, sy = 56 * 16, route = new[] { (50 * 16, 56 * 16) }, park = (48 * 16, 50 * 16) },
                new Task { name = "heavy", cat = "vehicle", type = "heavy", sx = 41 * 16, sy = 56 * 16, route = new[] { (50 * 16, 56 * 16) } },
            };
            var follow0 = cam ? cam.follow : null;
            if (Only == null || Only.Contains("grunt_road")) yield return ClearBridgeMouths();   // vehicles queue at the deck (DV8) and the idle squad steps aside; only a robot wades past a busy mouth
            foreach (var t in tasks)
                if (Only == null || Only.Split(',').Contains(t.Key)) yield return RunTask(t);
            if (cam) cam.follow = follow0;
            sb.AppendLine($"\nframes saved: {frames} in milestones/H0-parity/ (gp53_<unit>_<zone>.png, {Screen.width}x{Screen.height}, RTS camera at 70 m following the unit, day; each taken as the selected unit passes the middle of its zone)");
            if (shotLog.Length > 0) sb.AppendLine(shotLog.ToString());
            sb.AppendLine($"\n## Core of each zone (H0 round 3): frames 2 tiles or more inside both ends of their stretch\n\nTest mode {GameBoot.TestMode}, targetFrameRate {Application.targetFrameRate}. The whole zone vs its core; a core at 1.000 x Zod puts the whole-zone shortfall on the start / stop / zone-change frames.\n");
            sb.AppendLine("| unit | zone | frames | sim px/s | sim / Zod | core frames | core sim px/s | Zod px/s | core / Zod | core real-clock m/s |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var r in coreRows) sb.AppendLine(r);
            sb.AppendLine("\nStart and stop frames per run (px moved / sim s in that frame):\n");
            sb.Append(edgeLog);
            System.IO.File.WriteAllText(dir + $"/gp53_road_bridge_speeds{Label}.md", sb.ToString());
            Report = "done\n" + sb;
            Debug.Log("[GP53] " + Report);
            Destroy(gameObject);
#if UNITY_EDITOR
            if (AutoStop) { AutoStop = false; UnityEditor.EditorApplication.isPlaying = false; }
#endif
        }

        /// <summary>
        /// grunt waded (DV8: robots never queue, they wade over a busy deck). The map's red units near the bridge walk off to (61,50) first.</summary>
        IEnumerator ClearBridgeMouths()
        {
            var movers = W.Objects.Where(o => o.IsMobile && o.Owner == Team.Red && !o.IsMinion && !o.Destroyed && o.CenterX >= 36 * 16 && o.CenterX < 60 * 16 && o.CenterY >= 42 * 16 && o.CenterY < 72 * 16).ToList();
            int k = 0;
            foreach (var o in movers) { W.SendWaypoints(Team.Red, o, new List<ZWaypoint> { ZWaypoint.Move((60 + 2 * (k % 3)) * 16 + 8, (49 + 2 * (k / 3)) * 16 + 8) }); k++; }
            float guard = Time.realtimeSinceStartup;
            while (movers.Any(o => !o.Destroyed && (o.Waypoints.Count > 0 || o.Minions.Any(m => m.Waypoints.Count > 0))) && Time.realtimeSinceStartup - guard < 25f) yield return null;
            sb.AppendLine($"Cleared first: {movers.Count} red map unit(s) near the bridge walked to (60-64, 49-51) in {Time.realtimeSinceStartup - guard:0.0} s (run 1 at 15:55 found the red squad idling at the deck's south mouth and the grunt waded, DV8).\n");
        }

        IEnumerator RunTask(Task t)
        {
            bool robot = t.cat == "robot";
            var u = robot ? W.CreateObject("robot", t.type, t.sx - 8, t.sy - 8, Team.Red) : W.CreateObject(t.cat, t.type, t.sx - 16, t.sy - 16, Team.Red);
            u.MaxHealth = 1000000000; u.SetHealth(u.MaxHealth); u.Damage = 0;                  // never dies, never engages (a timing run)
            var v = g.SpawnUnitView(u);
            if (v == null) { sb.AppendLine($"| {t.name} | - | no view | | | | | | | |"); yield break; }
            if (cam) { cam.follow = v.transform; cam.SetHeightImmediate(70f); }
            if (!input) input = FindFirstObjectByType<PlayerInput>();
            if (input) { input.Selection.Clear(); input.Selection.Add(u); }                // the game's own selection ring + bracket mark the timed unit
            var mids = ZoneMids(t.sx, t.sy, t.route[0].x, t.route[0].y);
            var stretches = Stretches(t.sx, t.sy, t.route[0].x, t.route[0].y);
            int moving = 0; var firstFrames = new StringBuilder(); var lastFrames = new Queue<string>();
            float dirX = t.route[0].x - t.sx, dirY = t.route[0].y - t.sy, dirL = Mathf.Max(1f, Mathf.Sqrt(dirX * dirX + dirY * dirY)); dirX /= dirL; dirY /= dirL;
            var anim = v.GetComponentInChildren<Animator>(); var vm = v.GetComponentInChildren<VehicleMotion>();
            for (int i = 0; i < 30; i++) yield return null;                                   // the view settles
            W.SendWaypoints(Team.Red, u, t.route.Select(p => ZWaypoint.Move(p.x, p.y)).ToList());
            var zones = new Dictionary<string, Acc>(); var shot = new HashSet<string>();
            double px = u.PosX, py = u.PosY, st = W.Time; Vector3 vp = v.transform.position; float travel0 = vm && TravelF != null ? (float)TravelF.GetValue(vm) : 0f;
            string z0 = ZoneOf(u); float guard = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - guard < 90f)
            {
                yield return null;
                string z1 = ZoneOf(u);
                double d = System.Math.Sqrt((u.PosX - px) * (u.PosX - px) + (u.PosY - py) * (u.PosY - py)), sdt = W.Time - st;
                Vector3 p1 = v.transform.position; float dd = new Vector2(p1.x - vp.x, p1.z - vp.z).magnitude;
                float travel1 = vm && TravelF != null ? (float)TravelF.GetValue(vm) : 0f;
                if (z0 == z1 && u.IsMoving)                                                    // frames without a tick count too (real clock, like the parity REAL-clock rows)
                {
                    if (!zones.TryGetValue(z1, out var a)) zones[z1] = a = new Acc();
                    a.simPx += d; a.simT += sdt; a.realT += Time.unscaledDeltaTime; a.drawnM += dd; a.frames++;
                    if (robot && anim) a.animSum += anim.speed;
                    if (vm) a.travelM += Mathf.Abs(travel1 - travel0);
                    float along = (float)((u.CenterX - t.sx) * dirX + (u.CenterY - t.sy) * dirY);
                    foreach (var s in stretches)
                        if (s.z == z1 && along >= s.a && along <= s.b)
                        {
                            if (along >= s.a + CoreMargin && along <= s.b - CoreMargin) { a.coreSimPx += d; a.coreSimT += sdt; a.coreRealT += Time.unscaledDeltaTime; a.coreFrames++; }
                            break;
                        }
                    string fl = $"{z1} along {along:0.0} px: moved {d:0.000} px in {sdt:0.0000} sim s";
                    if (moving++ < 4) firstFrames.Append(" | ").Append(fl);
                    lastFrames.Enqueue(fl); if (lastFrames.Count > 4) lastFrames.Dequeue();
                    if (mids.TryGetValue(z1, out var zm) && along >= zm.mid && !shot.Contains(z1))
                    {
                        shot.Add(z1);
                        ScreenCapture.CaptureScreenshot($"{dir}/gp53_{t.name}_{z1.Replace(' ', '_')}.png"); frames++;
                        shotLog.AppendLine($"- gp53_{t.name}_{z1.Replace(' ', '_')}.png: unit centre tile ({u.CenterX >> 4},{u.CenterY >> 4}), {along - zm.a:0} px into a {zm.b - zm.a:0} px {z1} stretch (middle {zm.mid - zm.a:0}), selected {(input != null && input.Selection.Contains(u))}, camera {(cam ? cam.transform.position.y : 0f):0} m");
                    }
                }
                px = u.PosX; py = u.PosY; st = W.Time; vp = p1; travel0 = travel1; z0 = z1;
                if (u.Waypoints.Count == 0 && !u.IsMoving) break;
            }
            var zod = u.MoveSpeed;
            edgeLog.AppendLine($"- {t.name} {string.Join(" / ", stretches.Select(s => $"{s.z} {s.a:0}-{s.b:0} px"))}: first moving frames{firstFrames}; last moving frames | {string.Join(" | ", lastFrames)}");
            foreach (var kv in zones.OrderBy(k => k.Key == "sand" ? 0 : k.Key == "road" ? 1 : 2))
            {
                var a = kv.Value; double factor = kv.Key == "sand" ? 1.0 : kv.Key == "water" ? ZConst.WATER_SPEED : ZConst.ROAD_SPEED;
                double sim = a.simPx / a.simT, real = a.simPx / 8.0 / a.realT, drawn = a.drawnM / a.realT, zp = zod * factor;
                coreRows.Add($"| {t.name} | {kv.Key} | {a.frames} | {sim:0.000} | {sim / zp:0.0000} | {a.coreFrames} | {(a.coreSimT > 0 ? a.coreSimPx / a.coreSimT : 0):0.000} | {zp:0.000} | {(a.coreSimT > 0 ? a.coreSimPx / a.coreSimT / zp : 0):0.0000} | {(a.coreRealT > 0 ? a.coreSimPx / 8.0 / a.coreRealT : 0):0.000} |");
                string look, ratio;
                if (robot)
                {
                    double am = a.animSum / a.frames;
                    look = $"walk clip x{am:0.00}"; ratio = $"clip x per drawn m/s {am / System.Math.Max(0.01, drawn):0.00}";
                }
                else { double tr = a.travelM / a.realT; look = $"wheels / tracks {tr:0.00} m/s"; ratio = $"{tr / System.Math.Max(0.01, drawn):0.00}"; }
                sb.AppendLine($"| {t.name} | {kv.Key} | {a.frames} | {sim:0.000} | {zp:0.000} ({zod} x {factor}) | {sim / zp:0.000} | {real:0.000} | {drawn:0.000} | {look} | {ratio} |");
            }
            if (input) input.Selection.Remove(u);
            if (t.park.x > 0) { W.SendWaypoints(Team.Red, u, new List<ZWaypoint> { ZWaypoint.Move(t.park.x, t.park.y) }); guard = Time.realtimeSinceStartup; while (u.Waypoints.Count > 0 && Time.realtimeSinceStartup - guard < 20f) yield return null; }
        }
    }
}
#endif
