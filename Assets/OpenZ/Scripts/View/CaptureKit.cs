using System.Collections;
using System.Collections.Generic;
using System.IO;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenZ
{
    /// <summary>
    /// Video capture kit (plan/04 sec. 6 / 6b, BR17). Off in normal play: active with the command line `-showcase`,
    /// `-demo`, `-cinematic` or `-maptop`, or after Ctrl+Shift+F12 in game (editor and build).
    /// <list type="bullet">
    /// <item>F8 demo mode: the CPU plays both sides (a red Zod bot is added / removed). `-demo` starts with it on.</item>
    /// <item>F9 cycles the cinematic camera: orbit (selected unit, else the red fort), low chase behind the selected unit,
    /// low flyover fort to fort along the map, crane-up from the ground to the overview, back to the RTS camera. HUD and
    /// cursor hidden in the cinematic modes; eased, no jumps. `[` / `]` slower / faster. `-cinematic` starts in the flyover.</item>
    /// <item>F10 HUD toggle (OpenZHud), F11 pause the bots only, `+` / `-` game speed 1x / 2x / 4x, N a full day/night
    /// cycle in 60 s (again: back to the normal clock).</item>
    /// <item>`-level L02|L17` (build) loads that level from the menu; `-maptop` renders an orthographic top-down image of
    /// the level at midday, HUD hidden, pixel-aligned to the Zod 16 px grid (16 px = 2 m, 4096 px wide), into
    /// builds/captures/&lt;level&gt;_maptop.png, then quits (build) or logs (editor).</item>
    /// </list>
    /// </summary>
    public class CaptureKit : MonoBehaviour
    {
        public static CaptureKit I { get; private set; }
        public static bool Enabled { get; private set; }
        /// <summary>
        /// shows the painted sky (and fades it out when the camera is back to Rts).</summary>
        public static bool CineActive => I && (I.mode != Cine.Rts || I.cineOn);

        enum Cine { Rts, Orbit, Chase, Flyover, Crane }
        Cine mode = Cine.Rts;
        float speedK = 1f, modeT, orbitYaw;
        Vector3 camVel; Quaternion lastRot;
        bool botsPaused; readonly List<ZBot> savedBots = new List<ZBot>();
        bool fastDay; float savedCycle = 720f;
        static bool argsDone;
        float f7At = -10f;
        bool flyCustom, hold; Vector3 flyA, flyB;

        static bool Arg(string a) { foreach (var s in System.Environment.GetCommandLineArgs()) if (s.Equals(a, System.StringComparison.OrdinalIgnoreCase)) return true; return false; }
        static string ArgVal(string a) { var v = System.Environment.GetCommandLineArgs(); for (int i = 0; i < v.Length - 1; i++) if (v[i].Equals(a, System.StringComparison.OrdinalIgnoreCase)) return v[i + 1]; return null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I) return;
            var go = new GameObject("CaptureKit"); DontDestroyOnLoad(go); I = go.AddComponent<CaptureKit>();
            Enabled = !global::OpenZ.OzRelease.IsRelease && (Arg("-showcase") || Arg("-demo") || Arg("-cinematic") || Arg("-maptop"));
        }

        void Start() { if (!global::OpenZ.OzRelease.IsRelease) StartCoroutine(Args()); }

        IEnumerator Args()
        {
            if (argsDone) yield break;
            argsDone = true;
            var lvl = ArgVal("-level");
            if (Arg("-demo")) { PlayerPrefs.SetInt("oz_botmatch", 1); PlayerPrefs.Save(); }
            if (!string.IsNullOrEmpty(lvl) && !Application.isEditor)
            {
                string scene = lvl.ToUpper().Contains("17") ? "L17_CarPark" : "L02_Desert";
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != scene) UnityEngine.SceneManagement.SceneManager.LoadScene(scene);
            }
            float w = 0; while ((!ZGame.I || ZGame.I.World == null) && w < 60f) { w += Time.unscaledDeltaTime; yield return null; }
            if (!ZGame.I || ZGame.I.World == null) yield break;
            if (Arg("-demo")) SetDemo(true);
            if (Arg("-cinematic")) SetMode(Cine.Flyover);
            if (Arg("-maptop")) { yield return new WaitForSeconds(2f); yield return MapTop(); if (!Application.isEditor) Application.Quit(); }
        }

        // ------------------------------------------------------------------ keys
        void Update()
        {
            var kb = Keyboard.current; if (kb == null) return;
            bool ctrl = kb.ctrlKey.isPressed, shift = kb.shiftKey.isPressed;
            if (ctrl && shift && kb.f12Key.wasPressedThisFrame && !global::OpenZ.OzRelease.IsRelease) { Enabled = !Enabled; Debug.Log("[OpenZ] capture kit " + (Enabled ? "ON" : "off")); if (!Enabled) SetMode(Cine.Rts); }
            if (!Enabled || !ZGame.I || ZGame.I.World == null) return;
            var G = ZGame.I;
            if (kb.f8Key.wasPressedThisFrame) SetDemo(!IsDemo());
            if (kb.f9Key.wasPressedThisFrame) SetMode((Cine)(((int)mode + 1) % 5));
            if (kb.f11Key.wasPressedThisFrame) PauseBots(!botsPaused);
            if (kb.equalsKey.wasPressedThisFrame || kb.numpadPlusKey.wasPressedThisFrame) G.gameSpeed = G.gameSpeed >= 4f ? 4f : G.gameSpeed * 2f;
            if ((kb.minusKey.wasPressedThisFrame && Time.unscaledTime - f7At >= 2f) || kb.numpadMinusKey.wasPressedThisFrame) G.gameSpeed = G.gameSpeed <= 1f ? 1f : G.gameSpeed * 0.5f;
            if (kb.leftBracketKey.wasPressedThisFrame) speedK = Mathf.Max(0.25f, speedK * 0.7f);
            if (kb.rightBracketKey.wasPressedThisFrame) speedK = Mathf.Min(4f, speedK / 0.7f);
            if (kb.nKey.wasPressedThisFrame && !ctrl) FastDay(!fastDay);
            // F7 + number: showcase scenes (plan/04 6b), staged with the real sim by the gate battery's scenario code
            if (kb.f7Key.wasPressedThisFrame) { f7At = Time.unscaledTime; Debug.Log("[OpenZ] showcase: press 1-9, 0 (=10) or - (=11)"); }
            if (Time.unscaledTime - f7At < 2f)
            {
                int n = -1;
                var digits = new[] { kb.digit0Key, kb.digit1Key, kb.digit2Key, kb.digit3Key, kb.digit4Key, kb.digit5Key, kb.digit6Key, kb.digit7Key, kb.digit8Key, kb.digit9Key };
                for (int i = 0; i < 10; i++) if (digits[i].wasPressedThisFrame) n = i == 0 ? 10 : i;
                if (kb.minusKey.wasPressedThisFrame) n = 11;
                if (n > 0 && !GateBattery.Running) { f7At = -10f; var go = new GameObject("Showcase_" + n); go.AddComponent<GateBattery>().showcase = n; }
            }
        }

        // ------------------------------------------------------------------ demo / bots / time
        static bool IsDemo() { var W = ZGame.I.World; foreach (var b in W.Bots) if (b.Team == Team.Red) return true; return false; }

        void SetDemo(bool on)
        {
            var G = ZGame.I; var W = G.World;
            W.Bots.RemoveAll(b => b.Team == Team.Red);
            if (on) W.Bots.Add(new ZBot(W, Team.Red, G.seed + 202) { Difficulty = ZBotDifficulty.Normal });
            if (!on) PlayerPrefs.SetInt("oz_botmatch", 0);
            Debug.Log("[OpenZ] demo mode (CPU vs CPU) " + (on ? "ON" : "off"));
        }

        void PauseBots(bool pause)
        {
            var W = ZGame.I.World;
            if (pause) { savedBots.Clear(); savedBots.AddRange(W.Bots); W.Bots.Clear(); }
            else { W.Bots.Clear(); W.Bots.AddRange(savedBots); savedBots.Clear(); }
            botsPaused = pause; Debug.Log("[OpenZ] bots " + (pause ? "paused" : "running"));
        }

        void FastDay(bool on)
        {
            var dn = FindFirstObjectByType<DayNight>(); if (!dn) return;
            if (on) { savedCycle = dn.cycleSeconds; dn.cycleSeconds = 60f; dn.running = true; }
            else dn.cycleSeconds = savedCycle;
            fastDay = on;
        }

        // ------------------------------------------------------------------ cinematic camera
        void SetMode(Cine m)
        {
            mode = m; modeT = 0f;
            var rts = FindFirstObjectByType<RtsCamera>();
            if (rts) rts.enabled = m == Cine.Rts;
            var hud = FindFirstObjectByType<OpenZHud>(); if (hud) hud.SetVisible(m == Cine.Rts);
            Cursor.visible = m == Cine.Rts;
            var cam = rts ? rts.GetComponent<Camera>() : Camera.main;
            if (cam) { lastRot = cam.transform.rotation; orbitYaw = cam.transform.eulerAngles.y; }
            camVel = Vector3.zero;
            Debug.Log("[OpenZ] cinematic camera: " + m);
        }

        ZObject Subject()
        {
            var input = FindFirstObjectByType<PlayerInput>();
            if (input && input.Selection.Count > 0 && !input.Selection[0].Destroyed) return input.Selection[0];
            foreach (var o in ZGame.I.World.Objects) if (o.Kind == ZKind.Building && o.Owner == Team.Red && o.Type.StartsWith("fort")) return o;
            return null;
        }

        void LateUpdate()
        {
            if (mode == Cine.Rts || hold || !ZGame.I || ZGame.I.World == null) return;
            var rts = FindFirstObjectByType<RtsCamera>(); var cam = rts ? rts.GetComponent<Camera>() : Camera.main; if (!cam) return;
            var G = ZGame.I; var W = G.World;
            float dt = Time.captureFramerate > 0 ? 1f / Time.captureFramerate : Time.unscaledDeltaTime; modeT += dt * speedK;   // fixed step while recording
            if (cineOn) { CineUpdate(cam, rts, dt); return; }
            Vector3 pos, look;
            switch (mode)
            {
                case Cine.Orbit:
                {
                    var s = Subject(); var c = s != null ? G.CenterWorldSmooth(s) : Vector3.zero;
                    float r = s != null && s.Kind == ZKind.Building ? 30f : s != null && s.Kind == ZKind.Robot ? 9f : 16f;
                    orbitYaw += dt * speedK * 9f;
                    var q = Quaternion.Euler(24f, orbitYaw, 0);
                    look = c + Vector3.up * (s != null && s.Kind == ZKind.Robot ? 1.3f : 2f); pos = look - q * Vector3.forward * r;
                    break;
                }
                case Cine.Chase:
                {
                    var s = Subject(); if (s == null) { SetMode(Cine.Rts); return; }
                    var c = G.CenterWorldSmooth(s); var v = G.Views.TryGetValue(s.Id, out var uv) && uv ? uv.transform : null;
                    var fwd = v ? -v.forward : Vector3.forward; fwd.y = 0; if (fwd.sqrMagnitude < 1e-3f) fwd = Vector3.forward; fwd.Normalize();   // models face -z
                    float back = s.Kind == ZKind.Robot ? 6f : 10f, up = s.Kind == ZKind.Robot ? 2.5f : 4f;
                    pos = c - fwd * back + Vector3.up * up; look = c + fwd * 6f + Vector3.up;
                    break;
                }
                case Cine.Flyover:
                {
                    // fort to fort, low, along the diagonal of the map, then back (ping-pong), 60 s a leg at speed 1
                    Vector3 a = Vector3.zero, b = Vector3.zero;
                    if (flyCustom) { a = flyA; b = flyB; }
                    else
                    {
                        foreach (var o in W.Objects) if (o.Kind == ZKind.Building && o.Type.StartsWith("fort")) { if (o.Owner == Team.Red) a = G.CenterWorld(o); else if (o.Owner == Team.Blue) b = G.CenterWorld(o); }
                        if (a == Vector3.zero || b == Vector3.zero) { a = ZGame.PxToWorld(0, W.Map.HeightPix); b = ZGame.PxToWorld(W.Map.WidthPix, 0); }
                    }
                    float u = Mathf.PingPong(modeT / 60f, 1f); u = u * u * (3 - 2 * u);
                    var p = Vector3.Lerp(a, b, u); var dir = (b - a).normalized * (Mathf.Repeat(modeT / 60f, 2f) < 1f ? 1f : -1f);
                    pos = p - dir * 18f + Vector3.up * 16f; look = p + dir * 22f;
                    break;
                }
                default:   // Crane: from ground level up to the full overview over 20 s, then hold
                {
                    var s = Subject(); var c = s != null ? G.CenterWorld(s) : ZGame.PxToWorld(W.Map.WidthPix / 2, W.Map.HeightPix / 2);
                    float u = Mathf.Clamp01(modeT / 20f); u = u * u * (3 - 2 * u);
                    float h = Mathf.Lerp(1.6f, rts ? rts.maxHeight : 200f, u), back = Mathf.Lerp(14f, 90f, u);
                    pos = c + new Vector3(0, h, -back); look = c;
                    break;
                }
            }
            var t = cam.transform;
            t.position = Vector3.SmoothDamp(t.position, pos, ref camVel, 0.6f, Mathf.Infinity, dt);
            var want = Quaternion.LookRotation(look - t.position, Vector3.up);
            t.rotation = Quaternion.Slerp(t.rotation, want, 1f - Mathf.Exp(-dt * 3f));
        }

        // ------------------------------------------------------------------ menu preview flyby (UI1)
        /// <summary>Records the cinematic fort-to-fort flyover as JPG frames at a fixed step (Time.captureFramerate), HUD hidden,
        /// at the given time of day; ffmpeg turns them into Resources/OpenZ/Menu/&lt;level&gt;_preview.mp4 for the level cards.</summary>
        public IEnumerator RecordPreview(string dir, float seconds, int fps, float timeOfDay) { return RecordPreview(dir, seconds, fps, timeOfDay, 0, 0, 0); }

        /// <summary>
        /// (the main camera into a RenderTexture; 0 = the Game view size as before), only the first `frames` frames kept
        /// (0 = seconds x fps).</summary>
        public IEnumerator RecordPreview(string dir, float seconds, int fps, float timeOfDay, int width, int height, int frames)
        {
            System.IO.Directory.CreateDirectory(dir);
            foreach (var f in System.IO.Directory.GetFiles(dir, "*.jpg")) System.IO.File.Delete(f);
            var dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.running = false; dn.timeOfDay = timeOfDay; dn.Apply(timeOfDay); }
            Enabled = true; flyCustom = false; speedK = 60f / Mathf.Max(4f, seconds * 1.15f);
            SetMode(Cine.Flyover); modeT = 0f;
            BeginGrab(width, height);
            yield return new WaitForSecondsRealtime(2.5f);                                          // the camera settles onto the path
            Time.captureFramerate = fps;
            int n = frames > 0 ? frames : Mathf.RoundToInt(seconds * fps);
            yield return CaptureFrames(dir, n, fps);
            Time.captureFramerate = 0;
            EndGrab();
            SetMode(Cine.Rts);
            Debug.Log($"[OpenZ] preview frames: {n} -> {dir}");
        }

        /// <summary>
        /// SmoothDamp 0.6 s) on a custom leg a -> b: the eased leg from u0 to u1 is recorded in `seconds`; the camera first
        /// settles parked at u0 for `settle` s (real time, the staged battle starts meanwhile). The bots are paused so the
        /// staged units (<see cref="StageBattle"/>) fight where they were put.</summary>
        public IEnumerator RecordFlyby(string dir, float seconds, int fps, float timeOfDay, int width, int height, Vector3 a, Vector3 b, float u0, float u1, float settle)
        {
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*.jpg")) File.Delete(f);
            var dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.running = false; dn.timeOfDay = timeOfDay; dn.Apply(timeOfDay); }
            bool pausedHere = !botsPaused; if (pausedHere) PauseBots(true);
            Enabled = true; flyCustom = true; flyA = a; flyB = b;
            SetMode(Cine.Flyover); speedK = 0f; modeT = 60f * Mathf.Clamp01(u0);
            BeginGrab(width, height);
            yield return new WaitForSecondsRealtime(Mathf.Max(1.5f, settle));
            speedK = 60f * (Mathf.Clamp01(u1) - Mathf.Clamp01(u0)) / Mathf.Max(1f, seconds);
            Time.captureFramerate = fps;
            int n = Mathf.RoundToInt(seconds * fps);
            yield return CaptureFrames(dir, n, fps);
            Time.captureFramerate = 0;
            EndGrab();
            SetMode(Cine.Rts); flyCustom = false;
            if (pausedHere) PauseBots(false);
            Debug.Log($"[OpenZ] flyby frames: {n} -> {dir}");
        }

        /// <summary></summary>
        public System.Action<int> FrameHook;
        /// <summary>
        /// streets (no horizon), and with the flyby sky on the L17 fog banks are thinned to 20 % (the low-shot curtain fix).</summary>
        public static bool NoFlybySky;

        IEnumerator CaptureFrames(string dir, int n, int fps)
        {
            for (int i = 0; i < n; i++)
            {
                if (i > 0 && i % 30 == 0) yield return HeatHold(fps);                                // chunks of 30 frames (GPU rule)
                if (FrameHook != null) FrameHook(i);
                yield return new WaitForEndOfFrame();
                File.WriteAllBytes(Path.Combine(dir, $"f_{i:0000}.jpg"), GrabJpg());
            }
        }

        // the frame grab: the Game view (as before) or the main camera into a RenderTexture of the wanted size
        RenderTexture grabRt; Texture2D grabTex; Camera grabCam;
        void BeginGrab(int w, int h)
        {
            if (w <= 0 || h <= 0 || (Screen.width == w && Screen.height == h)) return;
            var rts = FindFirstObjectByType<RtsCamera>(); grabCam = rts ? rts.GetComponent<Camera>() : Camera.main; if (!grabCam) return;
            grabRt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "OpenZ_FlybyCapture" };
            grabRt.Create(); grabCam.targetTexture = grabRt;
            grabTex = new Texture2D(w, h, TextureFormat.RGB24, false);
            Debug.Log($"[OpenZ] flyby capture {w}x{h} (camera into a RenderTexture; Game view {Screen.width}x{Screen.height})");
        }
        byte[] GrabJpg()
        {
            // the Game view path (Game view set to the capture size): keeps the night lamp bloom, which the RenderTexture path loses
            if (grabRt == null) { var tex = ScreenCapture.CaptureScreenshotAsTexture(); var bytes = tex.EncodeToJPG(97); Destroy(tex); return bytes; }
            var prev = RenderTexture.active; RenderTexture.active = grabRt;
            grabTex.ReadPixels(new Rect(0, 0, grabRt.width, grabRt.height), 0, 0, false); grabTex.Apply(false);
            RenderTexture.active = prev;
            return grabTex.EncodeToJPG(97);
        }
        void EndGrab()
        {
            if (grabCam) grabCam.targetTexture = null;
            if (grabRt) { grabRt.Release(); Destroy(grabRt); }
            if (grabTex) Destroy(grabTex);
            grabRt = null; grabTex = null; grabCam = null;
        }

        static int GpuTemp()
        {
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType("OpenZ.EditorTools.GpuGuard"); if (t == null) continue;
                var p = t.GetProperty("LastTemp"); return p != null ? (int)p.GetValue(null) : -1;
            }
            return -1;
        }

        /// <summary>GPU at 80 C or more: freeze the recording (sim, camera path, rendering) until it is below 75 C.</summary>
        IEnumerator HeatHold(int fps)
        {
            int t = GpuTemp(); if (t < 80) yield break;
            var cam = grabCam ? grabCam : Camera.main;
            hold = true; float ts = Time.timeScale; Time.captureFramerate = 0; Time.timeScale = 0f; if (cam) cam.enabled = false;
            Debug.Log($"[OpenZ] flyby capture: GPU {t} C, holding until below 75 C");
            float w = 0f; while (GpuTemp() >= 75 && w < 300f) { w += Time.unscaledDeltaTime; yield return null; }
            if (cam) cam.enabled = true; Time.timeScale = ts; Time.captureFramerate = fps; hold = false;
            Debug.Log($"[OpenZ] flyby capture: resumed at GPU {GpuTemp()} C after {w:0} s");
        }

        /// <summary>
        /// (side b / r, a unit type, a tile); every unit gets hpK x its health so the fight lasts the clip, then each one is
        /// ordered to attack the nearest staged enemy. Returns a one-line summary.</summary>
        public static string StageBattle(string spec, float hpK)
        {
            var G = ZGame.I; if (!G || G.World == null) return "no game";
            var W = G.World; var units = new List<ZObject>(); int miss = 0;
            foreach (var part in spec.Split(';'))
            {
                var f = part.Trim().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries); if (f.Length < 4) continue;
                var team = f[0].StartsWith("r") ? Team.Red : Team.Blue; string type = f[1];
                int tx = int.Parse(f[2]), ty = int.Parse(f[3]);
                bool robot = ZBuildLists.IsRobot(type);
                if (!OzCheatSpawner.FreeTile(W, tx, ty, robot ? 1 : 2, robot, out int fx, out int fy)) { miss++; continue; }
                ZObject o = robot ? W.CreateRobotGroup(type, fx * 16, fy * 16, team)
                                  : W.CreateObject(ZBuildLists.IsCannon(type) ? "cannon" : "vehicle", type, fx * 16, fy * 16, team);
                if (o == null) { miss++; continue; }
                var all = new List<ZObject> { o }; if (robot) all.AddRange(o.Minions);
                foreach (var u in all) { if (hpK > 1f) { u.MaxHealth = Mathf.RoundToInt(u.MaxHealth * hpK); u.SetHealth(u.MaxHealth); } G.SpawnUnitView(u); }
                units.Add(o);
            }
            int orders = 0;
            foreach (var u in units)
            {
                ZObject best = null; long bd = long.MaxValue;
                foreach (var e in units)
                {
                    if (e.Destroyed || e.Owner == u.Owner) continue;
                    long dx = e.CenterX - u.CenterX, dy = e.CenterY - u.CenterY, d = dx * dx + dy * dy; if (d < bd) { bd = d; best = e; }
                }
                if (best != null && W.SendWaypoints(u.Owner, u, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = best.Id, x = best.CenterX, y = best.CenterY } })) orders++;
            }
            string s = $"flyby stage: {units.Count} units ({miss} without room), {orders} attack orders";
            Debug.Log("[OpenZ] " + s);
            return s;
        }

        // A Catmull-Rom camera spline and a look-at spline, arc-length
        // parametrised, one ease over the whole move (slow start, glide, slow settle), gentle banking into turns, a low Perlin
        // handheld drift (a short stronger kick on big explosions), and a minimum distance (buildings 13 m past their half size,
        // units 8 m) that only lifts the camera, eased. Frame by frame at a fixed step (Time.captureFramerate).
        bool cineOn, cineRun; Vector3[] cineCam, cineLook; float cineDur, cineT, cineBank, cineKick, cineLift, cineLiftVel;
        float[] cineArc, cineEase; Vector3 cinePrevFwd; Quaternion cineRot; bool cineRotInit;
        public static int CineFrames { get; private set; }
        public static float CineMinBuilding { get; private set; } = 999f;
        public static float CineMinUnit { get; private set; } = 999f;

        static Vector3 CatmullRom(Vector3[] p, float u)
        {
            int n = p.Length - 1; float f = Mathf.Clamp01(u) * n; int i = Mathf.Min(n - 1, (int)f); float t = f - i;
            Vector3 p1 = p[i], p2 = p[i + 1];
            Vector3 p0 = i > 0 ? p[i - 1] : p1 + (p1 - p2), p3 = i + 2 <= n ? p[i + 2] : p2 + (p2 - p1);
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        // arc-length table of the camera spline and the eased progress (velocity: smooth ramp up 30 %, glide, smooth ramp down 30 %)
        void CineTables()
        {
            const int N = 600;
            cineArc = new float[N + 1]; float len = 0f; var prev = CatmullRom(cineCam, 0f);
            for (int i = 1; i <= N; i++) { var q = CatmullRom(cineCam, i / (float)N); len += Vector3.Distance(prev, q); prev = q; cineArc[i] = len; }
            for (int i = 0; i <= N; i++) cineArc[i] = len > 0f ? cineArc[i] / len : i / (float)N;
            cineEase = new float[N + 1]; float acc = 0f;
            for (int i = 1; i <= N; i++)
            {
                float x = (i - 0.5f) / N, a = 0.3f;
                float v = x < a ? Mathf.SmoothStep(0.12f, 1f, x / a) : x > 1f - a ? Mathf.SmoothStep(0.1f, 1f, (1f - x) / a) : 1f;
                acc += v; cineEase[i] = acc;
            }
            for (int i = 0; i <= N; i++) cineEase[i] /= acc;
        }

        static float Table(float[] tab, float x)
        {
            int N = tab.Length - 1; float f = Mathf.Clamp01(x) * N; int i = Mathf.Min(N - 1, (int)f);
            return Mathf.Lerp(tab[i], tab[i + 1], f - i);
        }

        // arc fraction s -> spline parameter u (inverse of the arc table)
        float ArcToU(float s)
        {
            int lo = 0, hi = cineArc.Length - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (cineArc[m] < s) lo = m; else hi = m; }
            float span = cineArc[hi] - cineArc[lo], k = span > 1e-6f ? (s - cineArc[lo]) / span : 0f;
            return (lo + k) / (cineArc.Length - 1);
        }

        void CineUpdate(Camera cam, RtsCamera rts, float dt)
        {
            if (cineRun) cineT = Mathf.Min(cineDur, cineT + dt);
            float x = cineDur > 0f ? cineT / cineDur : 0f;
            float s = Table(cineEase, x), u = ArcToU(s);
            var pos = CatmullRom(cineCam, u);
            var look = CatmullRom(cineLook, Mathf.Min(1f, s + 0.03f));                                  // the look target leads the move a little
            // minimum distance: buildings 13 m past their half size, units 8 m; only lifts, eased (never a pop)
            var G = ZGame.I; float needLift = 0f, minB = 999f, minU = 999f;
            if (G && G.World != null)
                foreach (var o in G.World.Objects)
                {
                    if (o.Destroyed) continue;
                    bool bld = o.Kind == ZKind.Building && !o.IsBridge;
                    if (!bld && !o.IsUnit) continue;
                    var c = G.CenterWorld(o);
                    float half = bld ? Mathf.Max(o.WidthPix, o.HeightPix) / 16f : (o.Kind == ZKind.Robot ? 0.6f : 1.8f);
                    float wantD = half + (bld ? 13f : 8f), top = bld ? 6f : 1.5f;
                    var d = pos - c; float hz = new Vector2(d.x, d.z).magnitude;
                    float dist = Mathf.Sqrt(hz * hz + Mathf.Max(0f, pos.y - top) * Mathf.Max(0f, pos.y - top)) - half;
                    if (bld) minB = Mathf.Min(minB, dist); else minU = Mathf.Min(minU, dist + half);
                    if (hz < wantD) { float y = c.y + top + Mathf.Sqrt(wantD * wantD - hz * hz); needLift = Mathf.Max(needLift, y - pos.y); }
                }
            cineLift = Mathf.SmoothDamp(cineLift, Mathf.Max(0f, needLift), ref cineLiftVel, 0.9f, Mathf.Infinity, dt);
            pos.y += cineLift;
            if (cineRun) { CineMinBuilding = Mathf.Min(CineMinBuilding, minB); CineMinUnit = Mathf.Min(CineMinUnit, minU); }
            // handheld: slow Perlin drift, a short stronger kick when a big blast shook the RTS camera
            if (rts) cineKick = Mathf.Max(cineKick, Mathf.Clamp01(rts.TakeShake() * 2.5f));
            cineKick *= Mathf.Exp(-dt * 2.2f);
            float t = cineT + 17.3f, amp = 1f + 5f * cineKick;
            var drift = new Vector3(Mathf.PerlinNoise(t * 0.21f, 1.3f) - 0.5f, Mathf.PerlinNoise(4.1f, t * 0.17f) - 0.5f, Mathf.PerlinNoise(t * 0.19f, 7.7f) - 0.5f) * 0.32f * amp;
            drift += new Vector3(Mathf.PerlinNoise(t * 9f, 2.2f) - 0.5f, Mathf.PerlinNoise(3.3f, t * 8f) - 0.5f, 0f) * 0.22f * cineKick;
            pos += drift;
            var fwd = look - pos; if (fwd.sqrMagnitude < 1e-4f) fwd = cam.transform.forward;
            var want = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            // banking into turns: roll from the yaw rate (a few degrees at most)
            var flat = fwd; flat.y = 0f; flat.Normalize();
            if (cinePrevFwd.sqrMagnitude > 0f && dt > 0f)
            {
                float yawRate = Vector3.SignedAngle(cinePrevFwd, flat, Vector3.up) / dt;
                cineBank = Mathf.Lerp(cineBank, Mathf.Clamp(-yawRate * 0.35f, -4.5f, 4.5f), 1f - Mathf.Exp(-dt * 1.6f));
            }
            cinePrevFwd = flat;
            float hy = (Mathf.PerlinNoise(t * 0.27f, 11.1f) - 0.5f) * 0.6f * amp, hp = (Mathf.PerlinNoise(13.7f, t * 0.23f) - 0.5f) * 0.5f * amp;
            want = want * Quaternion.Euler(hp, hy, cineBank + (Mathf.PerlinNoise(t * 0.2f, 19.5f) - 0.5f) * 0.4f);
            cineRot = cineRotInit ? Quaternion.Slerp(cineRot, want, 1f - Mathf.Exp(-dt * 8f)) : want; cineRotInit = true;
            cam.transform.SetPositionAndRotation(pos, cineRot);
        }

        /// <summary><see cref="CineUpdate"/>
        /// width x height, `seconds` x fps frames at a fixed step; parked at the start for `settle` s first (real time: a staged battle
        /// gets going). Bots paused, the hit flash off for this session only (never the player's setting).</summary>
        public IEnumerator RecordCine(string dir, float seconds, int fps, float timeOfDay, int width, int height, Vector3[] cam, Vector3[] look, float settle)
        {
            Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir, "*.jpg")) File.Delete(f);
            var dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.running = false; dn.timeOfDay = timeOfDay; dn.Apply(timeOfDay); }
            bool pausedHere = !botsPaused; if (pausedHere) PauseBots(true);
            OpenZDamageStates.CaptureNoFlash = true;
            Enabled = true; SetMode(Cine.Flyover);
            cineCam = cam; cineLook = look; cineDur = seconds; cineT = 0f; cineRun = false; cineBank = 0f; cineKick = 0f; cineLift = 0f; cineLiftVel = 0f;
            cinePrevFwd = Vector3.zero; cineRotInit = false; CineMinBuilding = 999f; CineMinUnit = 999f;
            CineTables(); cineOn = true;
            BeginGrab(width, height);
            yield return new WaitForSecondsRealtime(Mathf.Max(1f, settle));
            cineRun = true; Time.captureFramerate = fps;
            int n = Mathf.RoundToInt(seconds * fps);
            yield return CaptureFrames(dir, n, fps);
            Time.captureFramerate = 0;
            EndGrab();
            cineOn = false; cineRun = false; SetMode(Cine.Rts);
            if (pausedHere) PauseBots(false);
            CineFrames = n;
            Debug.Log($"[OpenZ] cine flyby: {n} frames at {fps} fps -> {dir}; closest building {CineMinBuilding:0.0} m past its footprint, closest unit {CineMinUnit:0.0} m");
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        static string At(string side, string type, Vector3 w) => $"{side} {type} {Mathf.RoundToInt(w.x / 2f)} {Mathf.RoundToInt(-w.z / 2f)}; ";
        static string Row(string side, string[] types, Vector3 c, Vector3 along, float gap)
        {
            var s = ""; for (int i = 0; i < types.Length; i++) s += At(side, types[i], c + along * ((i - (types.Length - 1) * 0.5f) * gap)); return s;
        }

        /// <summary>
        /// #2 / #3 staged battles (L17 #2 = an MLRS battery firing full salvos at a tank group, the camera crossing the fire line
        /// between them). Positions from the level's own forts / bridges / flags; units staged like the P cheat (StageBattle).</summary>
        public IEnumerator MenuFlyby(int n, string dir, float seconds, int fps, int width, int height)
        {
            var G = ZGame.I; var W = G.World; bool city = G.level == "L17";
            Vector3 A = Vector3.zero, B = Vector3.zero;
            foreach (var o in W.Objects) if (o.Kind == ZKind.Building && o.Type.StartsWith("fort")) { if (o.Owner == Team.Red) A = G.CenterWorld(o); else if (o.Owner == Team.Blue) B = G.CenterWorld(o); }
            var mid = ZGame.PxToWorld(W.Map.WidthPix / 2, W.Map.HeightPix / 2);
            if (A == Vector3.zero || B == Vector3.zero) { A = ZGame.PxToWorld(0, W.Map.HeightPix); B = ZGame.PxToWorld(W.Map.WidthPix, 0); }
            A = Flat(A); B = Flat(B);
            Vector3 D = Flat(B - A).normalized, P = Vector3.Cross(Vector3.up, D), U = Vector3.up;
            float tod = city ? 0.62f : 0.12f;
            Vector3 C = mid; string spec = ""; float hpK = 3f, settle = 3f;
            Vector3[] cam, look;
            ZObject Nearest(System.Func<ZObject, bool> ok, Vector3 to)
            {
                ZObject best = null; float bd = float.MaxValue;
                foreach (var o in W.Objects) { if (!ok(o)) continue; float d = (Flat(G.CenterWorld(o)) - to).sqrMagnitude; if (d < bd) { bd = d; best = o; } }
                return best;
            }
            if (n == 1)
            {   // fort to fort: a rising glide past the red fort over the battlefield to a settle on the blue fort, never closer than ~40 m
                cam = new[] { A + D * 24f + P * 14f + U * 26f, Vector3.Lerp(A, B, 0.3f) + P * 18f + U * 22f, Vector3.Lerp(A, B, 0.62f) - P * 8f + U * 26f, B - D * 46f + P * 10f + U * 34f };
                look = new[] { A + D * 55f + U, Vector3.Lerp(A, B, 0.55f), Vector3.Lerp(A, B, 0.86f), B + U * 5f };
                if (city)
                {   // L17: from the half-way point (the first half is an empty car park at night)
                    cam = new[] { Vector3.Lerp(A, B, 0.5f) + P * 22f + U * 30f, Vector3.Lerp(A, B, 0.66f) + P * 18f + U * 26f, Vector3.Lerp(A, B, 0.8f) - P * 6f + U * 28f, B - D * 46f + P * 10f + U * 34f };
                    look = new[] { Vector3.Lerp(A, B, 0.62f) + U, Vector3.Lerp(A, B, 0.78f), Vector3.Lerp(A, B, 0.9f), B + U * 5f };
                }
                settle = 2f;
            }
            else
            {
                if (!city && n == 2)
                {   // the bridges: tanks + MLRS vs robot squads, mechs and the Electrobot
                    var br = Nearest(o => o.IsBridge, mid); if (br != null) C = Flat(G.CenterWorld(br));
                    spec = Row("r", new[] { "heavy", "light", "missile_launcher", "heavy" }, C - D * 9f, P, 5f) + Row("r", new[] { "grunt", "grunt" }, C - D * 14f, P, 6f)
                         + Row("b", new[] { "tough", "medium", "pyro", "medium", "laser" }, C + D * 9f, P, 4.5f) + At("b", ZElectro.Type, C + D * 13f);
                    cam = new[] { C + P * 46f - D * 28f + U * 34f, C + P * 31f - D * 10f + U * 26f, C + P * 25f + D * 9f + U * 22f, C + P * 33f + D * 28f + U * 29f };
                    look = new[] { C - D * 10f, C - D * 3f, C + D * 4f, C + D * 10f };
                }
                else if (!city)
                {   // the north-east plain round the blue factory: heavies + MLRS vs heavies + mechs, a rising crane-arc
                    var fl = Nearest(o => o.Type == "flag", ZGame.PxToWorld(W.Map.WidthPix * 85 / 100, W.Map.HeightPix * 12 / 100));   // take 1 (the blue factory) sat 20 m from the blue fort
                    C = fl != null ? Flat(G.CenterWorld(fl)) - P * 4f : Vector3.Lerp(A, B, 0.7f);
                    spec = Row("r", new[] { "heavy", "missile_launcher", "heavy", "missile_launcher" }, C - D * 9f, P, 5.5f) + Row("r", new[] { "tough", "pyro" }, C - D * 13f, P, 7f)
                         + Row("b", new[] { "heavy", "medium", "heavy", "medium" }, C + D * 8f, P, 5.5f) + Row("b", new[] { "laser", "grunt" }, C + D * 12f, P, 7f);
                    cam = new[] { C - P * 34f - D * 30f + U * 23f, C - P * 38f - D * 4f + U * 26f, C - P * 30f + D * 20f + U * 31f, C - P * 12f + D * 34f + U * 38f };
                    look = new[] { C - D * 7f, C, C + D * 5f, C + D * 8f };
                }
                else if (n == 2)
                {   // the south car park: an MLRS battery (4) firing full salvos at a tank group (heavy + light + mech firing back);
                    // the camera comes in from the side and crosses the fire line between them while the volleys are in the air
                    // (take 1 at the south edge: the tanks overran the battery and the camera ended over a cliff; take 2: both sides
                    // staged in separate calls = no attack orders, they hold and fire in range; a side dolly at 20-26 m)
                    var fl = Nearest(o => o.Type == "flag", ZGame.PxToWorld(W.Map.WidthPix * 89 / 100, W.Map.HeightPix * 84 / 100)); if (fl != null) C = Flat(G.CenterWorld(fl)) + D * 3f;
                    Debug.Log("[OpenZ] cine flyby MLRS battery: " + StageBattle(Row("r", new[] { "missile_launcher", "missile_launcher", "missile_launcher", "missile_launcher" }, C - D * 9f, P, 5.5f), 4f)
                              + " / tanks: " + StageBattle(Row("b", new[] { "light", "heavy", "medium", "heavy", "light" }, C + D * 9f, P, 5f), 4f));
                    settle = 3f;
                    cam = new[] { C + P * 40f - D * 22f + U * 26f, C + P * 28f - D * 8f + U * 21f, C + P * 24f + D * 6f + U * 20f, C + P * 30f + D * 20f + U * 24f };
                    look = new[] { C - D * 8f + U * 2f, C - D * 2f + U * 4f, C + D * 4f + U * 3f, C + D * 9f + U };
                }
                else
                {   // the central avenue: heavies + MLRS + pyro / psycho vs tanks + Electrobot + snipers, a tracking push along the street
                    var fl = Nearest(o => o.Type == "flag", mid); if (fl != null) C = Flat(G.CenterWorld(fl));
                    spec = Row("r", new[] { "heavy", "missile_launcher", "heavy" }, C - D * 9f, P, 5.5f) + Row("r", new[] { "pyro", "psycho" }, C - D * 13f, P, 7f)
                         + Row("b", new[] { "light", "heavy", "light" }, C + D * 8f, P, 5.5f) + At("b", ZElectro.Type, C + D * 12f) + At("b", "sniper", C + D * 13f + P * 6f);
                    cam = new[] { C - D * 42f + P * 16f + U * 31f, C - D * 20f + P * 20f + U * 25f, C + P * 22f + D * 2f + U * 22f, C + D * 24f + P * 14f + U * 30f };
                    look = new[] { C - D * 12f, C - D * 4f, C + D * 4f, C + D * 13f };
                }
                for (int i = 0; i < look.Length; i++) look[i] += U;
                Debug.Log($"[OpenZ] cine flyby {G.level} #{n}: battle at tile {Mathf.RoundToInt(C.x / 2f)},{Mathf.RoundToInt(-C.z / 2f)}; " + (spec.Length > 0 ? StageBattle(spec, hpK) : "staged above"));
            }
            yield return RecordCine(dir, seconds, fps, tod, width, height, cam, look, settle);
        }

        /// <summary>Last menu flyby result (Play proofs / the queue note).</summary>
        public static string LastMenuFlybyV2 = "none";
        float[] rainSave;                                                                                  // the storm take's rain tunables, put back after it

        // a look point `dist` m ahead of `c` along `fwd` (flat), `pitch` deg under the horizon (any height: only the direction counts)
        static Vector3 LookAt(Vector3 c, Vector3 fwd, float pitch, float dist) => c + fwd * dist + Vector3.down * (dist * Mathf.Tan(pitch * Mathf.Deg2Rad));

        // horizontal distance from p along d to the map rect (x 0..wM, z -hM..0) inset by m
        static float ToEdge(Vector3 p, Vector3 d, float wM, float hM, float m)
        {
            float t = 1e4f;
            if (d.x > 1e-4f) t = Mathf.Min(t, (wM - m - p.x) / d.x); else if (d.x < -1e-4f) t = Mathf.Min(t, (m - p.x) / d.x);
            if (d.z > 1e-4f) t = Mathf.Min(t, (-m - p.z) / d.z); else if (d.z < -1e-4f) t = Mathf.Min(t, (-hM + m - p.z) / d.z);
            return Mathf.Max(0f, t);
        }

        /// <summary>
        /// dolly toward the low sun: it starts looking down on the bridges and the river (26 deg), glides and tilts up so the drawn sun
        /// (FlybySky: HDR disc + bloom halo over the painted dawn sky) rises into the frame centre-right (the briefing's 7 deg offset) and
        /// ends on the horizon (2.5 deg under it). kind "storm" (L17): a forced rain thunderstorm at night with the fog banks (no flyby
        /// sky: the camera looks down into the streets), wet ground and puddles given time to build, a staged clash on the central
        /// avenue (a red armour column from the east, blue robots + a medium tank from the west, ~55 m apart, contact mid-clip), the
        /// camera gliding east along the avenue over the canal crossing at 16-20 m, two lightning flashes on cue (close / mid).
        /// storyboard = 8 frames along the path (no battle, short waits) to check the framing before the real take.</summary>
        public IEnumerator MenuFlybyV2(string kind, string dir, float seconds, int fps, int width, int height, float tod, bool storyboard)
        {
            var G = ZGame.I; var W = G.World;
            float wM = W.Map.WidthPix / 8f, hM = W.Map.HeightPix / 8f;
            var U = Vector3.up; Vector3[] cam, look; float settle = 2f;
            var dn = FindFirstObjectByType<DayNight>();
            if (storyboard) { seconds = 2f; fps = 4; }
            if (kind == "sunrise")
            {
                if (tod < 0f) tod = DayNight.SunriseStart;
                if (dn) { dn.running = false; dn.timeOfDay = tod; dn.Apply(tod); }
                var sun = RenderSettings.sun;
                var toSun = Flat(sun ? -sun.transform.forward : new Vector3(0.94f, 0.16f, -0.3f)).normalized;
                float yaw = Mathf.Atan2(toSun.x, toSun.z) * Mathf.Rad2Deg - ZodFlyover.LowSunOffsetDeg;   // the sun 7 deg right of centre
                Vector3 F = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, P = Vector3.Cross(U, F);
                Vector3 C = ZGame.PxToWorld(W.Map.WidthPix / 2, W.Map.HeightPix / 2);
                // while rising 36 -> 40 m (over the 28-30 m canyon rims, so the far mesas read from above like the briefing horizon shot)
                // and tilting 22 -> 3.5 deg: the river, the bridges and the palms first, then the sun rises into the frame over the mesas
                float back = Mathf.Min(62f, ToEdge(C, -F, wM, hM, 8f));
                cam = new[] { C - F * back + U * 36f, C - F * (back - 11f) + U * 37f + P * 1f, C - F * (back - 22f) + U * 38.5f + P * 1.5f, C - F * (back - 34f) + U * 40f + P * 1.5f };
                float[] pitch = { 22f, 15f, 7f, 3.5f }, shift = { 7f, 4f, 1.5f, 0f };
                look = new Vector3[4];
                for (int i = 0; i < 4; i++) look[i] = LookAt(cam[i] - P * shift[i], F, pitch[i], 120f);
                Debug.Log($"[OpenZ] menu flyby sunrise L02: tod {tod:F3}, sun yaw {yaw + ZodFlyover.LowSunOffsetDeg:0.0} deg, camera yaw {yaw:0.0}, centre {C.ToString("F0")}, back {back:0} m");
            }
            else
            {
                if (tod < 0f) tod = 0.45f;                                                                  // the approved take: dusk (sky in the puddles, lamps on)
                if (dn) { dn.running = false; dn.timeOfDay = tod; dn.Apply(tod); }
                var wx = OzWeather.I;
                if (wx)
                {   // capture only: heavier, brighter streaks so the rain reads in a 2560x1600 menu clip (restored after the take)
                    rainSave = new[] { wx.dropsAtFull, wx.rainAlpha, wx.rainGain, wx.streakLength, wx.dropWidth };
                    wx.dropsAtFull = 2400; wx.rainAlpha = 0.8f; wx.rainGain = 1.4f; wx.streakLength = 0.0085f; wx.dropWidth = 0.0012f;
                    if (!wx.StormActive) wx.ForceStorm(900f, false);
                    Debug.Log("[OpenZ] menu flyby storm: " + wx.Status());
                }
                else Debug.LogWarning("[OpenZ] menu flyby storm: no OzWeather in this level");
                NoFlybySky = true;
                float waited = 0f;                                                                          // rain fade-in 10 s + wet ground / puddles (12 s lag)
                while (waited < (storyboard ? 12f : 60f) && (!wx || wx.Wetness < 0.8f)) { waited += Time.deltaTime; yield return null; }
                Vector3 A = new Vector3(0f, 0f, -100f);                                                     // the central avenue (lamp rows at z -98 / -102)
                cam = new[] { A + new Vector3(62f, 19f, -18f), A + new Vector3(90f, 17f, -16f), A + new Vector3(118f, 16f, -14.5f), A + new Vector3(143f, 19f, -19f) };
                look = new[] { A + new Vector3(92f, 0f, 0f), A + new Vector3(116f, 0f, 1f), A + new Vector3(140f, 1f, 2f), A + new Vector3(161f, 2.5f, 4f) };
                if (!storyboard)
                {   // the approved take: ~38 m apart round the canal crossing, contact a few seconds in, right where the camera looks mid-clip
                    var E = Vector3.right; var Pz = Vector3.forward;
                    string red = Row("r", new[] { "light", "heavy", "light" }, A + E * 150f, Pz, 5f) + Row("r", new[] { "jeep", "missile_launcher" }, A + E * 157.5f, Pz, 6f);
                    string blue = Row("b", new[] { "tough", "medium", "grunt" }, A + E * 114f, Pz, 4f) + Row("b", new[] { "pyro", "psycho" }, A + E * 108f, Pz, 6f);
                    Debug.Log("[OpenZ] menu flyby storm clash: " + StageBattle(red + blue, 4f));
                    settle = 3f;
                }
                if (wx) FrameHook = i =>
                {
                    int a = Mathf.RoundToInt(seconds * fps * 0.2f), b = Mathf.RoundToInt(seconds * fps * 0.64f);
                    if (i == a) wx.ForceFlash(OzStormPlan.Close);
                    if (i == b) wx.ForceFlash(OzStormPlan.Mid);
                };
                Debug.Log("[OpenZ] menu flyby storm L17 before the take: " + (wx ? wx.Status() : "no weather"));
            }
            for (int i = 0; i < look.Length; i++) if (kind != "sunrise") look[i] += U;
            yield return RecordCine(dir, seconds, fps, tod, width, height, cam, look, settle);
            FrameHook = null; NoFlybySky = false;
            var w2 = OzWeather.I;
            if (kind != "sunrise" && w2 && rainSave != null)
            {
                w2.dropsAtFull = Mathf.RoundToInt(rainSave[0]); w2.rainAlpha = rainSave[1]; w2.rainGain = rainSave[2]; w2.streakLength = rainSave[3]; w2.dropWidth = rainSave[4];
                rainSave = null;
            }
            if (kind != "sunrise" && w2 && !storyboard) w2.StopStorm(3f);
            LastMenuFlybyV2 = $"{kind} {G.level} tod {tod:F3}: {CineFrames} frames -> {dir}; closest building {CineMinBuilding:0.0} m, unit {CineMinUnit:0.0} m";
            Debug.Log("[OpenZ] menu flyby v2: " + LastMenuFlybyV2);
        }

        // ------------------------------------------------------------------ -maptop
        public IEnumerator MapTop()
        {
            var G = ZGame.I; var W = G.World;
            var dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.running = false; dn.timeOfDay = 0.2f; dn.Apply(0.2f); }
            var hud = FindFirstObjectByType<OpenZHud>(); if (hud) hud.SetVisible(false);
            var atmo = FindFirstObjectByType<Atmosphere>(); bool atmoOn = atmo && atmo.enabled; if (atmo) atmo.enabled = false;   // no haze over the map photo
            yield return new WaitForSeconds(1.5f);
            Shader.SetGlobalFloat("_AtmoEnabled", 0f); Shader.SetGlobalFloat("_ShimmerAmount", 0f);
            float wM = W.Map.width * 2f, hM = W.Map.height * 2f;                             // 16 px tiles = 2 m
            int pw = 4096, ph = Mathf.RoundToInt(4096f * hM / wM);
            var go = new GameObject("MapTopCam"); var cam = go.AddComponent<Camera>();
            var main = Camera.main;
            if (main) cam.CopyFrom(main);
            cam.orthographic = true; cam.orthographicSize = hM * 0.5f; cam.aspect = wM / hM;
            cam.transform.SetPositionAndRotation(new Vector3(wM * 0.5f, 60f, -hM * 0.5f), Quaternion.Euler(90f, 0f, 0f));   // map x 0..w, z 0..-h
            cam.nearClipPlane = 0.5f; cam.farClipPlane = 200f;
            var rt = new RenderTexture(pw, ph, 24, RenderTextureFormat.ARGB32); rt.antiAliasing = 4;
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt; var tex = new Texture2D(pw, ph, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, pw, ph), 0, 0); tex.Apply(); RenderTexture.active = null;
            string dir = Application.isEditor ? Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../builds/captures")) : Path.GetFullPath(Path.Combine(Application.dataPath, "../../captures"));
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, G.level + "_maptop.png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null; Destroy(rt); Destroy(tex); Destroy(go);
            if (hud) hud.SetVisible(true);
            if (atmo) atmo.enabled = atmoOn;
            Debug.Log($"[OpenZ] -maptop: {file} ({pw}x{ph}, {wM}x{hM} m, 1 px = {wM / pw:F4} m)");
        }
    }
}
