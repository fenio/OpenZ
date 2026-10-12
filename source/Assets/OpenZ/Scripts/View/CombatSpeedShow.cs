#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// speed 1, 60 fps cap, L02 day, CPU bots cleared, shooters unkillable, targets unarmed (Damage 0) and immobile.
    ///
    /// Round 3 (the reviewer's fixes): staged on open sand in the middle of the map (rows 53-58, west of the (51,58) bridge road x 38-45 and
    /// east of it x 56-62), far from every map edge, in four lanes (rows 53 and 57 on each side), and every frame is framed by FitClear():
    /// the camera is moved until the shooters and targets project inside the clear part of the 2560x1600 screen (left of the unit panel and
    /// the minimap, below the top bar, above the group bar). Four stages, each one short Play run in the full look:
    /// A = grunt bolt, psycho MG, sniper crack, laser dashes (+ the sniper kill: round in the air, a frame burst around its arrival, thrown);
    /// B = pyro flame, jeep bullets, gatling bullets, light tank shell; C = heavy tank shell pair, gun cannon shell, howitzer lob;
    /// D = rockets: tough bazooka, medium (mech) rocket, MLRS salvo, missile cannon.
    /// Mode "look": frames (async GPU readback, no stall) + a 1x clip (1280x800 JPGs at 30 fps real time, for ffmpeg) of that stage.
    /// Mode "measure" (the GPU-light test mode, logic only): all stages, longer dwell, no frames: real-clock sim fire intervals (median and
    /// segment mean against Zod's window +- one 60 fps frame), the speed every drawn round flew at, shell / rocket flights MissileFired ->
    /// MissileExploded on the real clock against the sim flight and Zod's distance / attack_missile_speed, and every bullet kill: the sim
    /// death, BlastWave's throw time, the drawing code's arrival of the killing round and CombatView's real throw.
    /// Start: PlayerPrefs oz_gp3_auto = "look|A" (B, C, D) or "measure|ABCD" before entering Play on L02 (it leaves Play when done, editor
    /// </summary>
    [DefaultExecutionOrder(100)]
    public class CombatSpeedShow : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Mode = "look", Stages = "A";
        public static void Run(string mode = "look", string stages = "A") { Mode = mode; Stages = stages; Report = "running"; new GameObject("CombatSpeedShow").AddComponent<CombatSpeedShow>(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoStart()
        {
            string job = PlayerPrefs.GetString("oz_gp3_auto", "");
            if (string.IsNullOrEmpty(job)) return;
            PlayerPrefs.DeleteKey("oz_gp3_auto"); PlayerPrefs.Save();
            var p = job.Split('|');
            string mode = p[0] == "measure" ? "measure" : "look", stages = p.Length > 1 && p[1].Length > 0 ? p[1] : "A";
            if (mode == "measure" && GameBoot.TestMode) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60; }   // GPU-light test mode, at the 60 fps cap
            AutoStop = true; Run(mode, stages);
        }
        static bool AutoStop;
        static string Records => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records"));
        static string Tmp => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/tmp/h0_parity_r3"));

        // ------------------------------------------------------------------------------------------ lanes
        class Lane
        {
            public string cat, type, round; public int col, row, tcol, trow; public bool robotTarget;
            public ZObject shooter, target; public readonly HashSet<int> ids = new HashSet<int>(); public readonly List<ZObject> targets = new List<ZObject>();
        }
        const int WS = 38, WT = 45, ES = 56, ET = 62, R1 = 53, R2 = 57;   // open sand rows 53-58: west x 38-45, east x 56-62 (the hut at (60,55) sits between the east lanes)
        static Lane L(string cat, string type, string round, int col, int row, int tcol, bool robotTarget) =>
            new Lane { cat = cat, type = type, round = round, col = col, row = row, tcol = robotTarget ? tcol : tcol - 1, trow = row, robotTarget = robotTarget };   // a 2x2 target one tile nearer: every shooter in range (tough 120 px)
        static List<Lane> StageLanes(char s)
        {
            switch (s)
            {
                case 'A': return new List<Lane> { L("robot", "grunt", "grunt bolt", WS, R1, WT, true), L("robot", "psycho", "psycho tracer", WS, R2, WT, true),
                                                  L("robot", "sniper", "sniper crack", ES, R1, ET, true), L("robot", "laser", "laser bolt", ES, R2, ET, true) };
                case 'B': return new List<Lane> { L("robot", "pyro", "pyro flame", WS, R1, WT, true), L("vehicle", "jeep", "jeep round", WS, R2, WT, true),
                                                  L("cannon", "gatling", "gatling round", ES, R1, ET, true), L("vehicle", "light", null, ES, R2, ET, false) };
                case 'C': return new List<Lane> { L("vehicle", "heavy", null, WS, R1, WT, false), L("cannon", "gun", null, WS, R2, WT, false),
                                                  L("cannon", "howitzer", null, ES, R1, ET, false) };
                default:  return new List<Lane> { L("robot", "tough", null, WS, R1, WT, false), L("vehicle", "medium", null, WS, R2, WT, false),
                                                  L("vehicle", "missile_launcher", null, ES, R1, ET, false), L("cannon", "missile_cannon", null, ES, R2, ET, false) };
            }
        }

        ZGame g; ZWorld W; RtsCamera cam; Camera camC; DayNight dn; string dir; readonly StringBuilder sb = new StringBuilder(), frameLog = new StringBuilder(), killLog = new StringBuilder();
        readonly Dictionary<int, string> shooterType = new Dictionary<int, string>();
        readonly Dictionary<int, List<(float real, double sim)>> fireTimes = new Dictionary<int, List<(float, double)>>();
        readonly Dictionary<int, Queue<(float t, float simFlight, float dist)>> inFlight = new Dictionary<int, Queue<(float, float, float)>>();
        readonly List<(string type, float real, float sim, float zod, bool clean)> flights = new List<(string, float, float, float, bool)>();
        readonly List<float> hitches = new List<float>();
        readonly List<(float t, int shooter, ZEventType type)> recent = new List<(float, int, ZEventType)>();
        readonly List<(float t, int victim, int killer, int frame)> bulletKills = new List<(float, int, int, int)>();
        bool listening; int frames; string lastErr;
        readonly Dictionary<(int, int, int), int> impactFrame = new Dictionary<(int, int, int), int>();
        // the drawn launch of every sim shell / rocket: the view may hold it (UnitAim: turret / barrel onto the target first) and then
        // draw it on a SHORTER flight so it still lands on the sim's blast = a faster shell on screen
        readonly Dictionary<int, Queue<(float t, float sim)>> toDraw = new Dictionary<int, Queue<(float, float)>>();
        readonly List<(string type, float hold, float drawn, float sim)> drawnLaunch = new List<(string, float, float, float)>();
        int cosmetic;
        readonly StringBuilder aimLog = new StringBuilder();
        void NoteAim(List<Lane> lanes)
        {
            foreach (var l in lanes)
                foreach (var m in Members(l.shooter))
                    if (g.Views.TryGetValue(m.Id, out var sv) && sv && sv.GetComponent<UnitAim>() is UnitAim ua && ua.Shots > 0)
                        aimLog.AppendLine($"- {m.Type} #{m.Id}: {ua.Shots} / {ua.HeldCount} / {ua.MaxHoldMs:0} ms / {ua.MaxFireError:0.0} deg");
        }
        void OnLaunched(ZObject a, string type, float flight)
        {
            if (!listening || a == null || !toDraw.TryGetValue(a.Id, out var q)) return;
            while (q.Count > 0 && Time.unscaledTime - q.Peek().t > 2.5f) q.Dequeue();                     // never drawn (held past 2.5 s: none expected)
            if (q.Count == 0) { cosmetic++; return; }                                                     // MLRS ripple / twin-pod followers
            var s = q.Dequeue(); drawnLaunch.Add((type, Time.unscaledTime - s.t, flight, s.sim));
        }   // (killer, victim, death frame) -> the frame its drawn round arrives
        readonly List<Lane> all = new List<Lane>();

        void Update()
        {
            if (!listening || W == null) return;
            foreach (var d in BlastWave.DrawnKills)                                                     // the impact frame: the first frame at or after the drawn arrival (the spark's frame)
                if (Time.time >= d.at && !impactFrame.ContainsKey((d.killer, d.victim, d.frame))) impactFrame[(d.killer, d.victim, d.frame)] = Time.frameCount;
            float now = Time.unscaledTime;
            if (Time.unscaledDeltaTime > 0.03f) hitches.Add(now);
            foreach (var e in W.Events)
            {
                if ((e.type == ZEventType.Hitscan || e.type == ZEventType.MissileFired) && shooterType.ContainsKey(e.objId))
                {
                    if (!fireTimes.TryGetValue(e.objId, out var l)) fireTimes[e.objId] = l = new List<(float, double)>();
                    if (!(e.type == ZEventType.MissileFired && e.flag2)) l.Add((now, W.Time));                 // a heavy's second barrel is not a new cycle
                    recent.Add((now, e.objId, e.type));
                    if (e.type == ZEventType.MissileFired)
                    {
                        var a = W.GetAny(e.objId);
                        float d = a == null ? 0f : Mathf.Sqrt((e.x - a.CenterX) * (e.x - a.CenterX) + (e.y - a.CenterY) * (e.y - a.CenterY));
                        if (!inFlight.TryGetValue(e.objId, out var q)) inFlight[e.objId] = q = new Queue<(float, float, float)>();
                        q.Enqueue((now, e.value, d));
                        if (!e.flag) { if (!toDraw.TryGetValue(e.objId, out var dq)) toDraw[e.objId] = dq = new Queue<(float, float)>(); dq.Enqueue((now, e.value)); }
                    }
                }
                else if (e.type == ZEventType.MissileExploded && inFlight.TryGetValue(e.objId, out var q) && q.Count > 0)
                {
                    var f = q.Dequeue(); var a = W.GetAny(e.objId);
                    float zod = a != null && a.MissileSpeed > 0 ? f.dist / a.MissileSpeed : -1f;
                    bool clean = !hitches.Any(h => h >= f.t && h <= now);
                    flights.Add((shooterType[e.objId], now - f.t, f.simFlight, zod, clean));
                }
                else if (e.type == ZEventType.Death && shooterType.ContainsKey(e.otherId))
                {
                    foreach (var h in W.Events) if (h.type == ZEventType.Hitscan && h.objId == e.otherId && h.otherId == e.objId && h.flag) { bulletKills.Add((now, e.objId, e.otherId, Time.frameCount)); break; }
                }
            }
            if (recent.Count > 600) recent.RemoveRange(0, 300);
        }

        // ------------------------------------------------------------------------------------------ async capture (no frame stall)
        RenderTexture capFull, capSmall; readonly WaitForEndOfFrame eof = new WaitForEndOfFrame();
        readonly List<(string path, System.Func<string> what)> pngQueue = new List<(string, System.Func<string>)>();
        bool clipOn; int clipIdx; string clipDir; float clipNext; readonly StringBuilder clipTimes = new StringBuilder();
        static int pendingWrites;
        const float ClipFps = 30f;

        IEnumerator CaptureLoop()
        {
            while (true)
            {
                yield return eof;
                bool clip = clipOn && Time.unscaledTime >= clipNext;
                if (pngQueue.Count == 0 && !clip) continue;
                if (!capFull || capFull.width != Screen.width || capFull.height != Screen.height)
                {
                    if (capFull) capFull.Release();
                    capFull = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    capSmall = new RenderTexture(Screen.width / 2, Screen.height / 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                }
                ScreenCapture.CaptureScreenshotIntoRenderTexture(capFull);
                foreach (var q in pngQueue)
                {
                    Readback(capFull, q.path, true);
                    frameLog.AppendLine($"- {System.IO.Path.GetFileName(q.path)} ({Screen.width}x{Screen.height}, camera {cam.transform.position.y:0} m, frame {Time.frameCount}, t {Time.time:0.000}): {q.what()}");
                }
                pngQueue.Clear();
                if (clip)
                {
                    clipNext = Mathf.Max(clipNext + 1f / ClipFps, Time.unscaledTime + 0.5f / ClipFps);
                    Graphics.Blit(capFull, capSmall);
                    clipTimes.AppendLine($"f{clipIdx:00000}.jpg {Time.unscaledTime:0.00000}");
                    Readback(capSmall, $"{clipDir}/f{clipIdx:00000}.jpg", false); clipIdx++;
                }
            }
        }

        void Readback(RenderTexture rt, string path, bool png)
        {
            int w = rt.width, h = rt.height; bool flip = SystemInfo.graphicsUVStartsAtTop;
            Interlocked.Increment(ref pendingWrites);
            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req =>
            {
                if (req.hasError) { lastErr = "readback error " + path; Interlocked.Decrement(ref pendingWrites); return; }
                var data = req.GetData<byte>().ToArray();
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try
                    {
                        if (flip)
                        {
                            int row = w * 4; var tmp = new byte[row];
                            for (int y = 0; y < h / 2; y++)
                            {
                                System.Buffer.BlockCopy(data, y * row, tmp, 0, row);
                                System.Buffer.BlockCopy(data, (h - 1 - y) * row, data, y * row, row);
                                System.Buffer.BlockCopy(tmp, 0, data, (h - 1 - y) * row, row);
                            }
                        }
                        for (int i = 3; i < data.Length; i += 4) data[i] = 255;
                        var bytes = png ? ImageConversion.EncodeArrayToPNG(data, GraphicsFormat.R8G8B8A8_UNorm, (uint)w, (uint)h)
                                        : ImageConversion.EncodeArrayToJPG(data, GraphicsFormat.R8G8B8A8_UNorm, (uint)w, (uint)h, 0, 90);
                        System.IO.File.WriteAllBytes(path, bytes);
                    }
                    catch (System.Exception ex) { lastErr = ex.GetType().Name + ": " + ex.Message; }
                    finally { Interlocked.Decrement(ref pendingWrites); }
                });
            });
        }

        void Shot(string name, string what) => Shot(name, () => what);
        void Shot(string name, System.Func<string> what) { pngQueue.Add(($"{dir}/gp3_{name}.png", what)); frames++; }

        // ------------------------------------------------------------------------------------------ staging
        (int, int) Spot(int tx, int ty, bool robot)
        {
            for (int r = 0; r < 6; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                    {
                        int x = tx + dx, y = ty + dy;
                        bool ok = robot ? W.Path.TilePassable(x, y, true)
                                        : W.Path.TilePassable(x, y, false) && W.Path.TilePassable(x + 1, y, false) && W.Path.TilePassable(x, y + 1, false) && W.Path.TilePassable(x + 1, y + 1, false);
                        if (ok) return (x, y);
                    }
            return (tx, ty);
        }
        static IEnumerable<ZObject> Members(ZObject o) { yield return o; foreach (var m in o.Minions) if (m != null) yield return m; }
        ZObject Squad(string type, int tx, int ty, Team t)
        {
            var (x, y) = Spot(tx, ty, true);
            var o = W.CreateRobotGroup(type, x * 16, y * 16, t); g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }
        ZObject One(string cat, string type, int tx, int ty, Team t)
        {
            var (x, y) = Spot(tx, ty, cat == "robot");
            var o = W.CreateObject(cat, type, x * 16, y * 16, t); g.SpawnUnitView(o);
            return o;
        }
        void Kill(ZObject o) { if (o == null) return; foreach (var m in Members(o).ToList()) if (!m.Destroyed) { m.SetHealth(0); W.UpdateObjectHealth(m); } }
        void ClearArea()
        {   // robots and vehicles only (a killed cannon would burst in the stage)
            foreach (var o in new List<ZObject>(W.Objects))
                if ((o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle) && !o.Destroyed && o.CenterX / 16 >= 33 && o.CenterX / 16 <= 67 && o.CenterY / 16 >= 46 && o.CenterY / 16 <= 62) { o.SetHealth(0); W.UpdateObjectHealth(o); }
        }

        void Spawn(Lane l)
        {
            l.shooter = l.cat == "robot" ? Squad(l.type, l.col, l.row, Team.Red) : One(l.cat, l.type, l.col, l.row, Team.Red);
            l.target = l.robotTarget ? Squad("grunt", l.tcol, l.trow, Team.Blue) : One("vehicle", "heavy", l.tcol, l.trow, Team.Blue);
            foreach (var m in Members(l.shooter)) { m.MaxHealth = 100000000; m.SetHealth(m.MaxHealth); shooterType[m.Id] = m.Type; l.ids.Add(m.Id); }
            foreach (var m in Members(l.target)) { m.Damage = 0; m.MoveSpeed = 0; m.MaxHealth = 100000000; m.SetHealth(m.MaxHealth); l.targets.Add(m); }
        }
        void Attack(Lane l) => W.SendWaypoints(Team.Red, l.shooter, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = l.target.Id, x = l.target.CenterX, y = l.target.CenterY } });

        // ------------------------------------------------------------------------------------------ framing
        /// <summary>The clear part of the 2560x1600 screen, in screen pixels (y up): left of the unit panel (x > 1570 px) and the minimap,
        /// below the top bar + flag bar (PNG y < 270), above the group bar (PNG y > 1420).</summary>
        Rect ClearBox() { float sx = Screen.width / 2560f, sy = Screen.height / 1600f; return new Rect(110f * sx, 200f * sy, 1420f * sx, 1120f * sy); }
        IEnumerable<Vector3> PointsOf(IEnumerable<ZObject> objs)
        {
            foreach (var o in objs)
            {
                if (o == null) continue;
                var p = g.Views.TryGetValue(o.Id, out var v) && v ? v.transform.position : g.CenterWorld(o);
                float r = o.Kind == ZKind.Robot ? 0.6f : 2.2f, up = o.Kind == ZKind.Robot ? 2.1f : 2.6f;
                yield return p + new Vector3(-r, 0, -r); yield return p + new Vector3(r, 0, r); yield return p + new Vector3(-r, up, r); yield return p + new Vector3(r, up, -r);
            }
        }
        /// <summary>Moves the RTS camera so every point projects inside ClearBox (centred in it), at height h0 or higher if they do not fit.</summary>
        float FitClear(List<Vector3> pts, float h0, float hMax = 95f)
        {
            if (pts.Count == 0) return h0;
            cam.follow = null;
            var c = pts.Aggregate(Vector3.zero, (s, p) => s + p) / pts.Count;
            cam.focus = new Vector3(c.x, 0f, c.z - 0.0f); float h = h0; var box = ClearBox(); var pl = new Plane(Vector3.up, new Vector3(0f, c.y, 0f));
            for (int it = 0; it < 8; it++)
            {
                cam.SetHeightImmediate(h);
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                foreach (var p in pts) { var s = camC.WorldToScreenPoint(p); x0 = Mathf.Min(x0, s.x); x1 = Mathf.Max(x1, s.x); y0 = Mathf.Min(y0, s.y); y1 = Mathf.Max(y1, s.y); }
                var bb = Rect.MinMaxRect(x0, y0, x1, y1);
                float k = Mathf.Max(bb.width / (box.width * 0.88f), bb.height / (box.height * 0.88f));
                var r1 = camC.ScreenPointToRay(bb.center); var r2 = camC.ScreenPointToRay(box.center);
                if (pl.Raycast(r1, out float e1) && pl.Raycast(r2, out float e2)) { var w1 = r1.GetPoint(e1); var w2 = r2.GetPoint(e2); cam.focus += new Vector3(w1.x - w2.x, 0f, w1.z - w2.z); }
                if (k > 1.001f) h = Mathf.Min(hMax, h * Mathf.Min(1.6f, k * 1.02f));
            }
            cam.SetHeightImmediate(h);
            return h;
        }
        List<Vector3> LanePts(params Lane[] ls) => PointsOf(ls.SelectMany(l => Members(l.shooter).Concat(l.targets))).ToList();
        /// <summary>The lane and the other lane on its side of the road (west x 38-45 / east x 56-62): both are framed together, so the
        /// </summary>
        Lane[] WithSide(Lane l) => all.Where(o => o == l || ((o.col < 50) == (l.col < 50) && o.shooter != null && !o.shooter.Destroyed)).ToArray();

        int Drawn(string key) { var r = WeaponsV2Runner.I; return r && r.DrawnMps.TryGetValue(key, out var v) ? (int)v.y : 0; }
        /// <summary>Waits (real time, at most `max` s) until a round of `key` is drawn (hitscan) or a missile of the lane leaves.</summary>
        IEnumerator AfterShot(Lane l, float after, float max)
        {
            float t0 = Time.unscaledTime; int n0 = l.round != null ? Drawn(l.round) : 0;
            while (Time.unscaledTime - t0 < max)
            {
                if (l.round != null ? Drawn(l.round) > n0 : recent.Any(r => r.t >= t0 && l.ids.Contains(r.shooter) && r.type == ZEventType.MissileFired)) break;
                yield return null;
            }
            float t1 = Time.unscaledTime; while (Time.unscaledTime - t1 < after) yield return null;
        }
        IEnumerator Settle(float s) { float t = Time.unscaledTime; while (Time.unscaledTime - t < s) yield return null; }

        // ------------------------------------------------------------------------------------------ run
        IEnumerator Start()
        {
            g = ZGame.I; W = g?.World;
            if (W == null || g.level != "L02") { Report = "needs Play on L02"; Destroy(gameObject); yield break; }
            float wait0 = Time.realtimeSinceStartup;
            while ((ZodBriefing.BriefingActive || g.paused || W.Time < 2) && Time.realtimeSinceStartup - wait0 < 45f) yield return null;
            var cf = typeof(ZodBriefing).GetField("comms", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            while (ZodBriefing.I && cf != null && cf.GetValue(ZodBriefing.I) is ZodComms zc && zc.Visible && Time.realtimeSinceStartup - wait0 < 45f) yield return null;
            W.Bots.Clear();
            cam = FindFirstObjectByType<RtsCamera>(); camC = cam ? cam.GetComponent<Camera>() : Camera.main; dn = FindFirstObjectByType<DayNight>();
            if (dn) { dn.running = false; dn.timeOfDay = 0.08f; dn.Apply(0.08f); }
            dir = Records + "/milestones/H0-parity"; System.IO.Directory.CreateDirectory(dir); System.IO.Directory.CreateDirectory(Tmp);
            var run = WeaponsV2Runner.Ensure(); run.DrawnMps.Clear();
            BlastWave.BulletKills.Clear(); BlastWave.DrawnKills.Clear();
            bool look = Mode == "look";
            StartCoroutine(CaptureLoop());
            LegacyMatch.Launched += OnLaunched;
            sb.AppendLine($"# GP3 combat speed in Play, {(look ? "look" : "measure")} run {Stages} ({System.DateTime.Now:yyyy-MM-dd HH:mm:ss}, L02 day)\n");
            sb.AppendLine($"Test mode {GameBoot.TestMode} ({(GameBoot.TestMode ? "GPU-light: atmosphere, SSAO, shadows, MSAA off" : "full look")}), game speed {g.gameSpeed}, Time.timeScale {Time.timeScale}, Application.targetFrameRate {Application.targetFrameRate}, vSync {QualitySettings.vSyncCount}, screen {Screen.width}x{Screen.height}, GPU {SystemInfo.graphicsDeviceName}, graphicsUVStartsAtTop {SystemInfo.graphicsUVStartsAtTop}. " +
                          "All times on the REAL clock (Time.unscaledTime) unless marked sim. CPU bots cleared; shooters unkillable; targets unarmed (Damage 0) and immobile. Stage: open sand rows 53-58, west lanes x 38 -> 45, east lanes x 56 -> 62, rows 53 and 57.\n");
            if (!look)
            {   // capture check: the async path against ScreenCapture.CaptureScreenshot of the same frame (orientation, colour)
                yield return Settle(0.5f);
                pngQueue.Add((Tmp + "/capcheck_async.png", () => "capture check")); ScreenCapture.CaptureScreenshot(Tmp + "/capcheck_sync.png");
                yield return null; yield return null;
            }
            ClearArea(); yield return Settle(1f);
            foreach (char s in Stages)
            {
                if ("ABCD".IndexOf(s) < 0) continue;
                yield return look ? LookStage(s) : MeasureStage(s);
            }
            listening = false; clipOn = false;
            float w0 = Time.realtimeSinceStartup; while (pendingWrites > 0 && Time.realtimeSinceStartup - w0 < 30f) yield return null;
            WriteReport(run);
            if (dn) dn.running = true;
            Destroy(gameObject);
#if UNITY_EDITOR
            if (AutoStop) { AutoStop = false; UnityEditor.EditorApplication.isPlaying = false; }
#endif
        }

        IEnumerator LookStage(char s)
        {
            var lanes = StageLanes(s); all.Clear(); all.AddRange(lanes);
            foreach (var l in lanes) Spawn(l);
            foreach (var l in lanes) Attack(l);
            listening = true;
            clipDir = $"{Tmp}/clip_{s}"; System.IO.Directory.CreateDirectory(clipDir);
            clipIdx = 0; clipTimes.Clear(); clipNext = Time.unscaledTime; clipOn = true;
            float h = FitClear(LanePts(lanes.ToArray()), 70f);
            yield return Settle(2.0f);
            var key = lanes[0];
            yield return AfterShot(key, 0.15f, 5f);
            Shot($"{s}_rts", $"RTS camera {h:0} m (all {lanes.Count} lanes in the clear part of the screen): {string.Join(", ", lanes.Select(l => $"{l.type} (row {l.row}, {(l.col < 50 ? "west" : "east")})"))} firing east, 1x");
            yield return null;
            foreach (var l in lanes)
            {
                float hh = FitClear(LanePts(WithSide(l)), 26f);
                yield return Settle(0.35f);
                switch (l.type)
                {
                    case "sniper":
                        yield return AfterShot(l, 0.2f, 4f); Shot($"{s}_sniper_0", $"{hh:0} m: the sniper's crack 0.2 s after the shot (Zod 37.5 m/s)"); yield return null;
                        yield return AfterShot(l, 0.3f, 4f); Shot($"{s}_sniper_1", $"{hh:0} m: a second sniper crack 0.3 s after its shot"); yield return null;
                        break;
                    case "heavy":
                        yield return AfterShot(l, 0.2f, 6f); Shot($"{s}_heavy_0.20s", $"{hh:0} m: the heavy tank's FIRST shell of its pair, 0.2 s after firing (Zod 135 px/s; the DV30 second barrel fires 0.36 s after the first)"); yield return null;
                        yield return Settle(0.23f); Shot($"{s}_heavy_0.45s", $"{hh:0} m: the same pair at ~0.45 s: the first shell past mid-flight, the second shell just out of the other barrel"); yield return null;
                        break;
                    default:
                    {
                        bool missile = l.round == null;
                        float after = l.type == "missile_launcher" ? 0.45f : l.type == "howitzer" ? 0.45f : missile ? 0.2f : l.type == "pyro" ? 0.25f : 0.15f;
                        yield return AfterShot(l, after, 6f);
                        Shot($"{s}_{l.type}", $"{hh:0} m: {Desc(l.type)} {after:0.00} s after {(missile ? "firing" : "the drawn shot")}");
                        yield return null;
                        break;
                    }
                }
            }
            if (s == 'A') yield return KillSequence(lanes.First(l => l.type == "sniper"));
            clipOn = false;
            NoteAim(lanes);
            foreach (var l in lanes) { Kill(l.shooter); Kill(l.target); }
            System.IO.File.WriteAllText($"{clipDir}/times.txt", clipTimes.ToString());
            yield return Settle(0.5f);
        }

        static string Desc(string type)
        {
            switch (type)
            {
                case "grunt": return "a grunt bolt (shooter-project bolt, Zod 37.5 m/s)";
                case "psycho": return "the psycho's MG tracers (37.5 m/s)";
                case "laser": return "the laser's double-tap green dashes (37.5 m/s)";
                case "pyro": return "the pyro's flame stream (Zod flame 37.5 m/s)";
                case "jeep": return "the jeep's gun bolts (37.5 m/s)";
                case "gatling": return "the gatling cannon's MG tracers (37.5 m/s)";
                case "light": return "the light tank's shell (Zod 225 px/s)";
                case "gun": return "the gun cannon's shell (Zod 225 px/s)";
                case "howitzer": return "the howitzer's lobbed shell (Zod 95 px/s)";
                case "tough": return "the tough's bazooka rocket with back-blast (Zod 150 px/s)";
                case "medium": return "the medium tank (mech) rocket (Zod 160 px/s)";
                case "missile_launcher": return "the MLRS ripple salvo (Zod 70 px/s)";
                case "missile_cannon": return "the missile cannon's rocket (Zod 128 px/s)";
            }
            return type;
        }

        /// <summary>Stage A: one blue grunt leader at 1 HP in front of the sniper squad, framed before the shot: the round in the air, then
        /// EVERY frame from 2 before to 2 after the moment BlastWave throws the robot (= the drawn crack's arrival), then 0.35 s later.</summary>
        IEnumerator KillSequence(Lane l)
        {
            var victim = l.target; if (victim == null || victim.Destroyed) yield break;
            FitClear(LanePts(WithSide(l)), 26f); yield return Settle(0.4f);
            int k0 = bulletKills.Count; victim.SetHealth(1);
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 8f && !bulletKills.Skip(k0).Any(b => b.victim == victim.Id)) yield return null;
            var bk = bulletKills.Skip(k0).FirstOrDefault(b => b.victim == victim.Id);
            if (bk.victim != victim.Id) { killLog.AppendLine("- kill sequence: no bullet kill within 8 s"); yield break; }
            yield return null;                                                                            // BlastWave's record is written in the death frame's LateUpdate
            var rec = BlastWave.BulletKills.LastOrDefault(b => b.victim == victim.Id);
            float T = rec.victim == victim.Id ? rec.throwAt : Time.time + 0.3f, tDeath = rec.victim == victim.Id ? rec.deathAt : Time.time;
            g.Views.TryGetValue(victim.Id, out var vv);
            float mid = tDeath + (T - tDeath) * 0.5f;
            while (Time.time < mid) yield return null;
            Shot("A_kill_round_in_air", () => $"sniper kill on the blue grunt leader (1 HP), {Time.time - tDeath:0.000} s after the sim death: the crack is in the air, the victim still stands (thrown: {(vv && vv.Dying)}; the drawn crack arrives at +{T - tDeath:0.000} s)");
            var dk = BlastWave.DrawnKills.LastOrDefault(d => d.victim == victim.Id);
            if (dk.victim == victim.Id) T = dk.at;                                                       // the drawn crack's own arrival = the impact spark's time
            var burst = new List<string>();
            float fdt = Mathf.Max(0.008f, Time.smoothDeltaTime);
            while (Time.time < T - 2.5f * fdt) yield return null;
            for (int i = 1; i <= 5; i++)
            {
                string nm = $"A_kill_burst_{i}";
                Shot(nm, () => { string st = $"{(Time.time - T) * 1000f:+0;-0} ms from the drawn crack's arrival, victim thrown: {(vv && vv.Dying)}"; burst.Add($"{nm}: {st}"); return $"burst frame {i} of 5: {st}"; });
                yield return null;
            }
            while (Time.time < T + 0.35f) yield return null;
            Shot("A_kill_thrown", () => $"{Time.time - T:0.00} s after the crack's arrival: the victim is thrown (ragdoll) away from the shot (thrown: {(vv && vv.Dying)})");
            yield return null; yield return null;
            rec = BlastWave.BulletKills.LastOrDefault(b => b.victim == victim.Id);
            killLog.AppendLine($"- kill sequence: killer {W.GetAny(bk.killer)?.Type} #{bk.killer}, victim #{victim.Id}; muzzle -> body {rec.dist:0.00} m; sim death t={tDeath:0.000}; BlastWave throw due +{T - tDeath:0.000} s; drawn crack arrives +{(dk.victim == victim.Id ? dk.at - tDeath : -1f):0.000} s; thrown +{(rec.thrownAt > 0 ? rec.thrownAt - tDeath : -1f):0.000} s (frame {rec.thrownFrame}). Burst: {string.Join("; ", burst)}");
        }

        IEnumerator MeasureStage(char s)
        {
            var lanes = StageLanes(s); all.AddRange(lanes);
            foreach (var l in lanes) Spawn(l);
            foreach (var l in lanes) Attack(l);
            listening = true;
            FitClear(LanePts(lanes.ToArray()), 70f);
            float dwell = s == 'A' || s == 'B' ? 16f : 30f;
            yield return Settle(dwell);
            if (s == 'A' || s == 'B')
            {   // bullet kills of every hitscan type: the blue robots drop to 40 HP
                foreach (var l in lanes) if (l.robotTarget) foreach (var m in l.targets) if (!m.Destroyed) m.SetHealth(40);
                yield return Settle(6f);
            }
            NoteAim(lanes);
            foreach (var l in lanes) { Kill(l.shooter); Kill(l.target); }
            yield return Settle(1f);
        }

        // ------------------------------------------------------------------------------------------ report
        string GapList(IEnumerable<int> ids, bool sim)
        {
            var parts = new List<string>();
            foreach (var id in ids)
            {
                if (!fireTimes.TryGetValue(id, out var l)) continue;
                for (int i = 1; i < l.Count; i++) parts.Add((sim ? l[i].sim - l[i - 1].sim : l[i].real - l[i - 1].real).ToString("0.000"));
            }
            return string.Join(", ", parts);
        }

        void WriteReport(WeaponsV2Runner run)
        {
            var S = W.S; const float Frame = 1f / 60f;
            sb.AppendLine("## Fire interval (sim shots on the real clock)\n\nZod fires on the first server loop after the cooldown (10-15.6 ms loop): its interval is attack_speed .. attack_speed + 0.016 s. " +
                          "The Play clock stamps a shot on the frame that runs its tick, so the stated window is **Zod's window +- one 60 fps frame (16.7 ms)**. " +
                          "Segments = runs of shots between re-targets (a gap over max(1.6 x attack_speed, attack_speed + 0.2 s) starts a new one); segment mean = (last - first) / (shots - 1), weighted; " +
                          "median = the median of all gaps. In window = BOTH the median and the segment mean inside. Sim = the same gaps on the sim clock (W.Time).\n");
            sb.AppendLine("| shooter | units | shots | median gap s | segment mean s (gaps) | sim median s | Zod window s | window +- 1 frame s | in window |\n|---|---|---|---|---|---|---|---|---|");
            var slow = new StringBuilder();
            foreach (var grp in shooterType.GroupBy(kv => kv.Value))
            {
                var u = S.Unit(grp.Key); double zod = u != null ? u.attack_speed : 0; double split = System.Math.Max(1.6 * zod, zod + 0.2);
                var gaps = new List<double>(); var sims = new List<double>(); double segSum = 0; int segGaps = 0, n = 0;
                foreach (var kv in grp)
                {
                    if (!fireTimes.TryGetValue(kv.Key, out var l) || l.Count < 2) { if (l != null) n += l.Count; continue; }
                    n += l.Count; int a = 0;
                    for (int i = 1; i <= l.Count; i++)
                    {
                        bool end = i == l.Count || l[i].real - l[i - 1].real > split;
                        if (i < l.Count) { gaps.Add(l[i].real - l[i - 1].real); sims.Add(l[i].sim - l[i - 1].sim); }
                        if (end) { if (i - 1 > a) { segSum += l[i - 1].real - l[a].real; segGaps += i - 1 - a; } a = i; }
                    }
                }
                gaps.Sort(); sims.Sort();
                double med = gaps.Count > 0 ? gaps[gaps.Count / 2] : double.NaN, smed = sims.Count > 0 ? sims[sims.Count / 2] : double.NaN, mean = segGaps > 0 ? segSum / segGaps : double.NaN;
                double lo = zod - Frame, hi = zod + 0.016 + Frame;
                bool inWin = !double.IsNaN(med) && !double.IsNaN(mean) && med >= lo && med <= hi && mean >= lo && mean <= hi;
                if (zod >= 1.0) slow.AppendLine("- " + grp.Key + ": real gaps " + GapList(grp.Select(kv => kv.Key), false) + " s; sim gaps " + GapList(grp.Select(kv => kv.Key), true) + " s");
                sb.AppendLine($"| {grp.Key} | {grp.Count()} | {n} | {(double.IsNaN(med) ? "n/a" : med.ToString("0.0000"))} | {(double.IsNaN(mean) ? "n/a" : mean.ToString("0.0000"))} ({segGaps}) | {(double.IsNaN(smed) ? "n/a" : smed.ToString("0.0000"))} | {zod:0.000}-{zod + 0.016:0.000} | {lo:0.0000}-{hi:0.0000} | {(double.IsNaN(med) ? "n/a (too few shots)" : inWin ? "yes" : "NO")} |");
            }
            if (slow.Length > 0) sb.AppendLine("\nEvery gap of the slow shooters (attack_speed 1 s or more):\n").Append(slow);
            sb.AppendLine("\n## Drawn rounds (the speed each round / flame was drawn at: WeaponsV2Runner.DrawnMps, LegacyMatch.Shot, OpenZPyroFire)\n\n| weapon | rounds | mean m/s | max m/s | Zod m/s |\n|---|---|---|---|---|");
            foreach (var kv in run.DrawnMps) sb.AppendLine($"| {kv.Key} | {kv.Value.y:0} | {kv.Value.x / Mathf.Max(1f, kv.Value.y):0.00} | {kv.Value.z:0.00} | {CombatView.ZodBulletMps * g.gameSpeed:0.00} (300 px/s: ebullet.cpp:7, elaser.cpp:9, eflame.cpp:10) |");
            sb.AppendLine("\n## Shell and rocket flights (real clock MissileFired -> MissileExploded)\n\nClean = no frame over 30 ms between the fire and the blast. The sim flight is what the view draws the shell / rocket for; Zod = distance / attack_missile_speed.\n\n" +
                          "| shooter | flights (clean) | mean real s, clean | mean sim flight s | mean Zod s | real / sim, clean | real / sim, all | sim / Zod |\n|---|---|---|---|---|---|---|---|");
            foreach (var grp in flights.GroupBy(f => f.type))
            {
                var cl = grp.Where(f => f.clean).ToList();
                float sm = grp.Average(f => f.sim), z = grp.Average(f => f.zod), ra = grp.Average(f => f.real / Mathf.Max(0.001f, f.sim)), sz = grp.Average(f => f.sim / Mathf.Max(0.001f, f.zod));
                string r = cl.Count > 0 ? cl.Average(f => f.real).ToString("0.000") : "n/a (no clean flight)", rc = cl.Count > 0 ? cl.Average(f => f.real / Mathf.Max(0.001f, f.sim)).ToString("0.000") : "n/a (no clean flight)";
                sb.AppendLine($"| {grp.Key} | {grp.Count()} ({cl.Count}) | {r} | {sm:0.000} | {z:0.000} | {rc} | {ra:0.000} | {sz:0.000} |");
            }
            sb.AppendLine($"\nFrames over 30 ms in the run: {hitches.Count}.\n");
            sb.AppendLine("## Drawn launch of each sim shell / rocket (LegacyMatch.Launched)\n\nHold = real s from the sim's MissileFired until the view drew it (UnitAim holds a shot until the turret / barrel is on target; the drawn flight is then shortened so the shell still lands on the sim's blast). " +
                          "Drawn speed x Zod = sim flight / drawn flight (1.00 = the shell flies at Zod's attack_missile_speed on screen).\n");
            sb.AppendLine("| shooter | drawn | held > 1 frame | mean hold s | max hold s | mean drawn flight s | mean sim flight s | drawn speed x Zod, mean | max |\n|---|---|---|---|---|---|---|---|---|");
            foreach (var grp in drawnLaunch.GroupBy(x => x.type))
            {
                var l = grp.ToList(); int held = l.Count(x => x.hold > Frame + 0.002f);
                sb.AppendLine($"| {grp.Key} | {l.Count} | {held} | {l.Average(x => x.hold):0.000} | {l.Max(x => x.hold):0.000} | {l.Average(x => x.drawn):0.000} | {l.Average(x => x.sim):0.000} | {l.Average(x => x.sim / Mathf.Max(0.01f, x.drawn)):0.00} | {l.Max(x => x.sim / Mathf.Max(0.01f, x.drawn)):0.00} |");
            }
            sb.AppendLine($"\nCosmetic follower launches (MLRS ripple rockets 2-8, the mech's and missile cannon's twin pods): {cosmetic}.\n");
            sb.AppendLine("UnitAim per shooter (shots drawn / held / longest hold ms / largest aim error at the muzzle deg):\n");
            sb.Append(aimLog).AppendLine();
            sb.AppendLine("## Robot deaths by a round (BlastWave.BulletDelay, H0 round 3)\n\nPer kill: the drawn round's flight from the muzzle it left to the body point (the drawing code notes when its killing round arrives), the throw BlastWave scheduled, and the frame CombatView really threw the robot. Delta = thrown - drawn arrival.\n");
            sb.AppendLine("| killer | muzzle -> body m | drawn arrival s | BlastWave estimate s | thrown s | thrown - drawn ms | impact frame | throw frame | same frame |\n|---|---|---|---|---|---|---|---|---|");
            int ok = 0, nk = 0;
            foreach (var b in BlastWave.BulletKills)
            {
                var dk = BlastWave.DrawnKills.Where(x => x.victim == b.victim && x.killer == b.killer && x.frame == b.frame).ToList();
                float d = dk.Count > 0 ? dk[dk.Count - 1].at : -1f;
                int imp = dk.Count > 0 && impactFrame.TryGetValue((b.killer, b.victim, b.frame), out var fi) ? fi : -1;
                string dd = d > 0 ? (d - b.deathAt).ToString("0.000") : "not noted", th = b.thrownAt > 0 ? (b.thrownAt - b.deathAt).ToString("0.000") : (b.throwAt - b.deathAt < 0.016f ? "at once" : "n/a");
                string delta = d > 0 && b.thrownAt > 0 ? ((b.thrownAt - d) * 1000f).ToString("0") : "n/a";
                bool same = imp > 0 && b.thrownFrame == imp;
                if (d > 0 && b.thrownAt > 0) { nk++; if (same) ok++; }
                sb.AppendLine($"| {b.type} | {b.dist:0.00} | {dd} | {b.throwAt - b.deathAt:0.000} | {th} | {delta} | {(imp > 0 ? imp.ToString() : "n/a")} | {(b.thrownFrame > 0 ? b.thrownFrame.ToString() : "n/a")} | {(imp > 0 && b.thrownFrame > 0 ? (same ? "yes" : "NO") : "n/a")} |");
            }
            sb.AppendLine($"\n{nk} held kills with both times: {ok} thrown ON the frame their drawn round arrives (impact frame = the first frame at or after the arrival; the spark uses the same test).\n");
            if (killLog.Length > 0) sb.Append(killLog).AppendLine();
            if (frameLog.Length > 0) { sb.AppendLine("## Frames (1x, no slow motion; async GPU readback, no frame stall)\n"); sb.Append(frameLog); }
            if (lastErr != null) sb.AppendLine($"\nCapture error: {lastErr}");
            sb.Append(PyroDiag());
            System.IO.File.WriteAllText($"{dir}/gp3_run_{Mode}_{Stages}.md", sb.ToString());
            Report = "done\n" + sb;
            Debug.Log("[GP3] " + Mode + " " + Stages + " done: " + frames + " frames, " + flights.Count + " flights, " + BlastWave.BulletKills.Count + " bullet kills");
        }

        void OnDestroy() { LegacyMatch.Launched -= OnLaunched; if (capFull) capFull.Release(); if (capSmall) capSmall.Release(); }

        /// <summary>
        /// alight (OpenZPyroFire.burns) and every registered touchable bigger than 7 m.</summary>
        static string PyroDiag()
        {
            var pf = FindFirstObjectByType<OpenZPyroFire>(); if (!pf) return "";
            var s = new StringBuilder("\n## Pyro sticky fire (diagnostic)\n\n");
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            try
            {
                if (typeof(OpenZPyroFire).GetField("burns", F)?.GetValue(pf) is System.Collections.IDictionary burns)
                    foreach (System.Collections.DictionaryEntry de in burns)
                    {
                        var b = de.Value; var bt = b.GetType();
                        var go = bt.GetField("go")?.GetValue(b) as GameObject;
                        s.AppendLine($"- burn {de.Key}: {(go ? go.name : "null")} touched {bt.GetField("touched")?.GetValue(b)} structure {bt.GetField("structure")?.GetValue(b)} box {Box(go)}");
                    }
                if (typeof(OpenZPyroFire).GetField("touchables", F)?.GetValue(pf) is System.Collections.IList list)
                {
                    int big = 0;
                    foreach (var tc in list)
                    {
                        var tt = tc.GetType(); var bb = (Bounds)tt.GetField("b").GetValue(tc); var go = tt.GetField("go").GetValue(tc) as GameObject;
                        if (bb.size.x > 7f || bb.size.z > 7f) { big++; s.AppendLine($"- big touchable {tt.GetField("key").GetValue(tc)}: {(go ? go.name : "null")} centre {bb.center:F0} size {bb.size:F0}"); }
                    }
                    s.AppendLine($"\n{list.Count} touchables, {big} bigger than 7 m.");
                }
            }
            catch (System.Exception ex) { s.AppendLine("diag error " + ex.Message); }
            return s.ToString();
        }
        static string Box(GameObject go)
        {
            if (!go) return "-";
            var rs = go.GetComponentsInChildren<Renderer>(false); if (rs.Length == 0) return "no renderer";
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return $"centre {b.center:F0} size {b.size:F0} ({rs.Length} renderers)";
        }
    }
}
#endif
