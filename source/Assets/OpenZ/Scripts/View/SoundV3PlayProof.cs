#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// (same for L17), GPU-light test mode, music off, 30 fps while it runs (heat). Three short runs per level:
    ///   cov   = AudioDirector.FireEveryEvent (all 508 events through their real path): every sound_v3 event must report audio, vol > 0.
    ///   fight = SoundV3Proof.Begin, then real sim units in five staged scenes on screen: F1 our fort attacked (fort gun track / fire /
    ///           reload, alarm, defender, blue grenades at our robots, heavy shells knocking robots down, heavy tank treads), F2 weapons
    ///           (Tough squad + mech volley together, jeep, sniper squad vs a manned jeep = sniped driver, gun / howitzer / missile cannon /
    ///           gatling / MLRS / tanks turning onto targets, a manned light tank destroyed, a tank halting and pivoting), F3 buildings
    ///           (an enemy radar turning, our factory producing and rolling out, one of our buildings destroyed, a building on fire),
    ///           F4 a bridge destroyed and rebuilt (repair timer shortened for the harness), F5 three empty guns captured; SoundV3Proof.Report.
    ///   ui    = SoundV3Proof.Begin, then the UI walk with queued Input System events (real mouse / keys): hover, right-click orders
    ///           (capture, enter, pickup, repair), EXIT CREW (E), Shift add / remove, double-click, HUD group store / recall / empty,
    ///           rally tool (arm, Shift chain, Esc, CLEAR), a greyed button, the P cheat (hover, drag to the map, drop on a panel), the pause
    ///           menu EFFECTS / VOICE sliders, then a WIN (L02) / LOSS (L17) and the end screen; Report, then the coverage sweep.
    /// </summary>
    [DefaultExecutionOrder(20)]
    public class SoundV3PlayProof : MonoBehaviour
    {
        static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones/sound_v3"));
        string job, level, part, Dir; float lastMark; bool done; int errors; float hot = -1f, lastSample = -1f; int peak = -1;
        readonly StringBuilder log = new StringBuilder();
        readonly List<string> verdicts = new List<string>();
        float v0Effects = -1f, v0Voice = -1f;
        // the OzSoundLog ring keeps only the last 2000 entries (~90 s of a fight): this run keeps its own complete copy
        readonly List<OzSoundLog.Entry> full = new List<OzSoundLog.Entry>();
        float pulledT = -1f; readonly HashSet<string> atPulled = new HashSet<string>();
        readonly Dictionary<string, int> caBase = new Dictionary<string, int>();
        float beginT = -1f;
        static string Sig(OzSoundLog.Entry e) => $"{e.t:R}|{e.src}|{e.key}|{e.clip}|{e.outcome}|{e.vol:R}|{e.dist:R}";
        void Pull()
        {
            if (beginT < 0f) return;
            var es = OzSoundLog.Since(Mathf.Max(beginT, pulledT));
            float maxT = pulledT; var edge = new HashSet<string>();
            foreach (var e in es)
            {
                string sig = Sig(e);
                if (e.t <= pulledT && atPulled.Contains(sig)) continue;
                full.Add(e);
                if (e.t > maxT) { maxT = e.t; edge.Clear(); }
                if (e.t >= maxT) edge.Add(sig);
            }
            if (maxT > pulledT) { pulledT = maxT; atPulled.Clear(); }
            foreach (var x in edge) atPulled.Add(x);
        }
        void Begin()
        {
            SoundV3Proof.Begin(); beginT = Time.unscaledTime; pulledT = beginT; full.Clear();
            caBase.Clear(); var ca = CombatAudio.I; if (ca) foreach (var kv in ca.PlayCounts) caBase[kv.Key] = kv.Value;
        }
        void LateUpdate() { Pull(); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Auto()
        {
            string job = PlayerPrefs.GetString("oz_sv3_auto", "");
            if (string.IsNullOrEmpty(job)) return;
            PlayerPrefs.DeleteKey("oz_sv3_auto"); PlayerPrefs.Save();
            var go = new GameObject("SoundV3PlayProof"); DontDestroyOnLoad(go);
            go.AddComponent<SoundV3PlayProof>().job = job;
        }

        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        float T => (float)clock.Elapsed.TotalSeconds;
        void Mark(string s) { lastMark = T; log.AppendLine($"[{T,7:0.00}] ## {s}"); Debug.Log("[sv3proof] " + s); }
        void Verdict(string step, bool ok, string what) { string v = $"{step}: {(ok ? "PASS" : "FAIL")} - {what}"; verdicts.Add(v); Mark("VERDICT " + v); }
        IEnumerator Wait(float s) { float end = T + s; while (T < end && !done) yield return null; }
        static IEnumerator F(int n) { for (int i = 0; i < n; i++) yield return null; }

        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; if (!done) Finish("disabled"); }
        void OnLog(string c, string st, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception) { errors++; log.AppendLine($"[{T,7:0.00}] ERROR {c}"); }
        }

        // ---------------------------------------------------------------- heat
        static int GpuTemp()
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType("OpenZ.EditorTools.GpuGuard"); if (t == null) continue;
                var p = t.GetProperty("LastTemp"); return p != null ? (int)p.GetValue(null) : -1;
            }
            return -1;
        }
        IEnumerator Heat()
        {
            while (!done)
            {
                int gpu = GpuTemp(); peak = Mathf.Max(peak, gpu);
                float step = lastSample < 0f ? 0.5f : T - lastSample; lastSample = T;
                if (gpu >= 90) { Finish("GPU 90 C"); yield break; }
                if (gpu >= 86) { hot = hot < 0f ? step : hot + step; if (hot >= 30f) { Finish("GPU >= 86 C for 30 s"); yield break; } }
                else if (gpu >= 0 && gpu < 84) hot = -1f;
                if (T - lastMark > 150f) { Finish("watchdog: no progress for 150 s"); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        // ---------------------------------------------------------------- sim helpers (real units, real orders)
        ZGame g; ZWorld W; PlayerInput input; RtsCamera cam; OpenZHud hud; AudioDirector ad; Team me, them;
        readonly List<ZObject> staged = new List<ZObject>();
        ZObject Fort(Team t) { foreach (var o in W.Objects) if (o.IsFort && o.Owner == t && !o.Destroyed) return o; return null; }
        static void Unkillable(ZObject o) { o.MaxHealth = 1000000000; o.SetHealth(o.MaxHealth); }
        ZObject Squad(string type, Team t, int px, int py, bool unkillable = true, int grenades = 0)
        {
            OzCheatSpawner.FreeTile(W, px >> 4, py >> 4, 1, true, out int tx, out int ty);
            var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t); if (o == null) return null;
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            staged.Add(o); staged.AddRange(o.Minions);
            foreach (var m in new[] { o }.Concat(o.Minions)) { if (unkillable) Unkillable(m); if (grenades > 0) m.Grenades = grenades; }
            return o;
        }
        ZObject Machine(string cat, string type, Team t, int px, int py, bool unkillable = true)
        {
            OzCheatSpawner.FreeTile(W, px >> 4, py >> 4, 2, false, out int tx, out int ty);
            var o = W.CreateObject(cat, type, tx * 16, ty * 16, t); if (o == null) return null;
            g.SpawnUnitView(o); staged.Add(o); if (unkillable) Unkillable(o); return o;
        }
        bool Attack(ZObject who, ZObject target) => who != null && target != null &&
            W.SendWaypoints(who.Owner, who, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = target.Id, x = target.CenterX, y = target.CenterY } });
        bool MoveTo(ZObject who, int px, int py) => who != null &&
            W.SendWaypoints(who.Owner, who, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Move, refId = -1, x = px, y = py, playerGiven = true } });
        bool OrderOn(ZObject who, ZObject target) => who != null && target != null &&
            W.SendWaypoints(who.Owner, who, new List<ZWaypoint> { input.OrderFor(who, target, target.CenterX, target.CenterY) });
        void Kill(ZObject m) { if (m != null && !m.Destroyed && W.Get(m.Id) == m) { m.SetHealth(0); W.UpdateObjectHealth(m); } }
        void KillStaged() { foreach (var m in new List<ZObject>(staged)) Kill(m); staged.Clear(); }
        void Focus(int px, int py, float h = 50f) { if (!cam) return; var p = ZGame.PxToWorld(px, py); cam.focus = new Vector3(p.x, 0f, p.z); cam.SetHeightImmediate(h); }
        void Shot(string name)
        {
            try { ScreenCapture.CaptureScreenshot(Path.Combine(Dir, name + ".png")); Mark($"frame {name}.png (test-mode context frame, {Screen.width}x{Screen.height}, camera height {(cam ? cam.height : -1f):0} m)"); }
            catch (Exception e) { Mark("shot failed " + e.Message); }
        }
        static int D(ZObject a, int x, int y) => Mathf.Abs(a.CenterX - x) + Mathf.Abs(a.CenterY - y);
        string FortGuns()
        {
            var sb = new StringBuilder(string.Join("; ", W.Objects.Where(o => o.IsFort && o.Owner == me).Select(o => $"{o.Type} #{o.Id} gun target {(o.FortGunTarget != null ? "#" + o.FortGunTarget.Id : "none")} reload {o.FortGunReload01:0.00}")));
            const BindingFlags NP = BindingFlags.Instance | BindingFlags.NonPublic;
            foreach (var v in FindObjectsByType<FortGunView>(FindObjectsSortMode.None))
            {
                var ring = typeof(FortGunView).GetField("ringBack", NP)?.GetValue(v) as LineRenderer;
                var yawT = typeof(FortGunView).GetField("yaw", NP)?.GetValue(v) as Transform;
                bool tossed = (bool)(typeof(FortGunView).GetField("tossed", NP)?.GetValue(v) ?? false);
                var dict = typeof(OzSoundV3).GetField("forts", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as System.Collections.IDictionary;
                object st = v.Fort != null && dict != null && dict.Contains(v.Fort.Id) ? dict[v.Fort.Id] : null;
                string pk = st != null ? st.GetType().GetField("prevK").GetValue(st).ToString() : "-";
                sb.Append($" | view fort #{v.Fort?.Id} ring {(ring ? ring.enabled.ToString() : "null")} tossed {tossed} yaw on screen {(yawT ? OzSoundV3.OnScreen(yawT.position, 0.1f).ToString() : "no yaw")} v3 prevK {pk}");
            }
            return sb.ToString();
        }
        ZObject FlagOf(ZObject b) { foreach (var o in W.Objects) if (o.Type == "flag" && o.LinkedObjects.Contains(b)) return o; return null; }
        int Plays(string id, float since) { Pull(); return full.Count(e => e.t >= since && e.key == id && e.outcome != null && e.outcome.StartsWith("PLAY")); }
        int AdPlays(string id) => ad != null && ad.Plays.TryGetValue(id, out var n) ? n : 0;
        string PlaysOf(float since, params string[] ids) => string.Join(", ", ids.Select(i => $"{i} {Plays(i, since)}{(AdPlays(i) > 0 ? "/ad" + AdPlays(i) : "")}"));

        // ---------------------------------------------------------------- real input (queued Input System state events)
        static Vector2 mousePos; static bool lDown, rDown;
        static void MouseState(Vector2 p, bool left, bool right)
        {
            mousePos = p; lDown = left; rDown = right;
            var st = new MouseState { position = p }; if (left) st = st.WithButton(MouseButton.Left); if (right) st = st.WithButton(MouseButton.Right);
            InputSystem.QueueStateEvent(UnityEngine.InputSystem.Mouse.current, st);
        }
        static Key[] held = new Key[0];
        static void Keys(params Key[] keys) { held = keys; InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys)); }
        IEnumerator MoveMouse(Vector2 to, int steps = 6)
        {
            Vector2 from = mousePos;
            for (int k = 1; k <= steps; k++) { MouseState(Vector2.Lerp(from, to, k / (float)steps), lDown, rDown); yield return F(1); }
            yield return F(2);
        }
        IEnumerator Click(Vector2 p, bool right = false)
        {
            yield return MoveMouse(p, 4);
            MouseState(p, !right, right); yield return F(3);
            MouseState(p, false, false); yield return F(4);
        }
        IEnumerator Tap(Key k, params Key[] mods)
        {
            var down = mods.Concat(new[] { k }).ToArray();
            Keys(down); yield return F(3); Keys(mods); yield return F(3);
        }
        Vector2 ScreenOf(ZObject o) => input.ScreenRect(o).center;
        bool hoverOk;
        /// <summary>Moves the mouse onto o (its screen rect centre, then small offsets) until PlayerInput.Hover is o.</summary>
        IEnumerator HoverOn(ZObject o)
        {
            hoverOk = false; if (o == null) yield break;
            var offs = new[] { Vector2.zero, new Vector2(0, 10), new Vector2(0, -10), new Vector2(10, 0), new Vector2(-10, 0), new Vector2(0, 22), new Vector2(0, -22), new Vector2(14, 14), new Vector2(-14, -14) };
            foreach (var d in offs)
            {
                yield return MoveMouse(ScreenOf(o) + d, 3); yield return F(3);
                if (input.Hover == o) { hoverOk = true; yield break; }
            }
        }
        Vector2 ScreenOfPx(int px, int py) { var w = Camera.main.WorldToScreenPoint(ZGame.PxToWorld(px, py) + Vector3.up * 0.2f); return new Vector2(w.x, w.y); }
        static Vector2 Centre(RectTransform rt) => RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
        static T Field<T>(object o, string name) where T : class
        {
            var f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return f != null ? f.GetValue(o) as T : null;
        }

        // ---------------------------------------------------------------- the run
        IEnumerator Start()
        {
            var parts = job.Split(':'); level = parts[0]; part = parts.Length > 1 ? parts[1] : "fight";
            Dir = Path.Combine(Root, level + "_" + part, "run_" + DateTime.Now.ToString("HHmm")); Directory.CreateDirectory(Dir);   // one folder per run: no run overwrites another lastMark = T;
            Mark($"sound_v3 Play proof {job} {DateTime.Now:yyyy-MM-dd HH:mm:ss}, scene {SceneManager.GetActiveScene().name}, oz_testmode {PlayerPrefs.GetInt("oz_testmode", 0)}, oz_music {PlayerPrefs.GetInt("oz_music", 1)}, screen {Screen.width}x{Screen.height}");
            StartCoroutine(Heat());
            float w0 = T;
            while ((!ZGame.I || ZGame.I.World == null || !AudioDirector.I || !FindFirstObjectByType<PlayerInput>() || !OpenZHud.I) && T - w0 < 100f) { lastMark = T; yield return null; }
            if (!ZGame.I || ZGame.I.World == null || !AudioDirector.I) { Finish("the level never came up within 100 s"); yield break; }
            g = ZGame.I; W = g.World; input = FindFirstObjectByType<PlayerInput>(); cam = FindFirstObjectByType<RtsCamera>(); ad = AudioDirector.I; hud = OpenZHud.I;
            me = g.player; them = me == Team.Red ? Team.Blue : Team.Red;
            Application.targetFrameRate = part == "fight" ? 24 : 30;                                                              // heat: the sound logic does not need 60 fps
            int bots = W.Bots.Count; W.Bots.Clear();
            Mark($"level up after {T - w0:0.0} s; CPU bots cleared ({bots}); player {me}; OzSoundV3.Enabled {OzSoundV3.Enabled}; 30 fps cap for the run");
            yield return Wait(3f);
            if (part == "cov") yield return Cov();
            else if (part == "ui") yield return Ui();
            else yield return Fight();
            if (!done) Finish(null);
        }

        // ================================================================ cov: FireEveryEvent
        IEnumerator Cov()
        {
            string path = Path.Combine(Dir, "fire_every_event.md");
            Mark("FireEveryEvent -> fire_every_event.md");
            yield return StartCoroutine(ad.FireEveryEvent(path));
            string[] lines = File.Exists(path) ? File.ReadAllLines(path) : new string[0];
            var rows = new Dictionary<string, string[]>();
            foreach (var l in lines) { if (!l.StartsWith("| ") || l.StartsWith("| event") ) continue; var c = l.Split('|').Select(x => x.Trim()).ToArray(); if (c.Length > 6) rows[c[1]] = c; }
            var bad = new List<string>(); var sb = new StringBuilder();
            foreach (var id in SoundV3Proof.NewEvents)
            {
                if (!rows.TryGetValue(id, out var c)) { bad.Add(id + " (no row)"); continue; }
                if (c[5] != "audio") bad.Add($"{id} ({c[5]}: {c[6]})"); else sb.Append($"{id} [{c[6]}]; ");
            }
            var dtl = rows.Keys.Where(k => k.StartsWith("dtl_")).Select(k => $"{k} {rows[k][5]}").ToList();
            string head = lines.FirstOrDefault(l => l.StartsWith("Fire-every-event")) ?? "(no summary line)";
            Mark("summary: " + head);
            Verdict("C1 every sound_v3 event produced audio (vol > 0)", bad.Count == 0, $"{SoundV3Proof.NewEvents.Length - bad.Count}/{SoundV3Proof.NewEvents.Length} audio; not: {string.Join(", ", bad)}");
            Verdict("C2 no FAIL in the whole table", lines.All(l => !l.Contains("| FAIL |")), head);
            Mark("details_v1 staged events: " + string.Join(", ", dtl));
            log.AppendLine("sound_v3 rows: " + sb);
        }

        // ================================================================ fight
        float fightT0;
        IEnumerator Fight()
        {
            Begin(); fightT0 = Time.unscaledTime;
            var ourFort = Fort(me); var theirFort = Fort(them);
            if (ourFort == null || theirFort == null) { Finish("no forts"); yield break; }
            int dx = Math.Sign(theirFort.CenterX - ourFort.CenterX), dy = Math.Sign(theirFort.CenterY - ourFort.CenterY);
            int mx = (ourFort.CenterX + theirFort.CenterX) / 2, my = (ourFort.CenterY + theirFort.CenterY) / 2;

            // ---- F1: our fort attacked by vehicles from two sides (the gun swings to each new target), a heavy tank drives in
            float f1 = Time.unscaledTime;
            int fx = ourFort.CenterX, fy = ourFort.CenterY;
            int cleared = 0;                                                                                 // the level's own enemies near our fort would hold the gun's target
            foreach (var o in W.Objects.ToList())
                if (o.Owner == them && !o.Destroyed && (o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon) && D(o, fx, fy) < 900) { Kill(o); cleared++; }
            Mark($"F1: {cleared} enemy units near our fort removed (the fort gun starts without a target)");
            yield return Wait(3f);
            var redG = Squad("grunt", me, fx + dx * 80, fy + dy * 70);
            var lightA = Machine("vehicle", "light", them, fx + dx * 150, fy + dy * 100);
            var blueHeavy = Machine("vehicle", "heavy", them, fx + dx * 330, fy + dy * 240);                  // drives in (treads)
            Focus(fx + dx * 90, fy + dy * 60, 55f);
            Mark($"F1 fort: our fort #{ourFort.Id} ({fx >> 4},{fy >> 4}); blue light A #{lightA?.Id} -> fort, blue heavy #{blueHeavy?.Id} drives in -> red grunts #{redG?.Id}; all unkillable");
            yield return Wait(0.5f);
            Attack(lightA, redG); Attack(blueHeavy, redG);                                   // never the fort itself: its gun would be shot off (tossed) and stop firing
            yield return Wait(8f); Shot("F1_fort_fight");
            yield return Wait(6f);
            var lightB = Machine("vehicle", "light", them, fx + dx * 170, fy - dy * 10);                       // a second bearing (~35 deg off A), same radius
            Attack(lightB, redG); Kill(lightA);
            Mark($"F1: light A killed, blue light B #{lightB?.Id} from another bearing; {FortGuns()}");
            yield return Wait(12f);
            var lightC = Machine("vehicle", "light", them, fx + dx * 40, fy + dy * 175);                       // a third bearing
            Attack(lightC, redG); Kill(lightB);
            Mark($"F1: light B killed, blue light C #{lightC?.Id}; {FortGuns()}");
            yield return Wait(9f); Mark("F1: " + FortGuns());
            yield return Wait(9f); Mark("F1: " + FortGuns());
            Mark("F1 plays: " + PlaysOf(f1, "veh_fort_traverse_start", "veh_fort_traverse_loop", "veh_fort_traverse_stop", "veh_fort_elevate", "wpn_fort_ready", "wpn_target_lock", "ui_fort_alarm", "bld_fort_lift",
                "rob_knockdown_land", "rob_getup", "imp_shrapnel_tink", "veh_tracks_heavy_loop", "shell_flight", "wpn_breech_clank", "imp_bullet_sand", "imp_bullet_stone", "dst_debris_concrete"));
            KillStaged(); yield return Wait(3f);

            if (part == "fort") yield break;                                                                 // "fort" = the F1 scene only (a re-run after a fix)

            // ---- G: a blue grenade squad at our grunts on open ground (throw at the release, fuse, clinks, Alan's warning)
            float fg = Time.unscaledTime;
            var gRed = Squad("grunt", me, mx - 40 * dx, my + 40);
            var gBlue = Squad("grunt", them, mx + 40 * dx, my + 40, true, 3);
            Focus(mx, my + 40, 45f);
            yield return Wait(0.5f);
            Attack(gBlue, gRed);
            Mark($"G grenades: blue grunts #{gBlue?.Id} (3 grenades each) -> red grunts #{gRed?.Id}, both unkillable, camera 45 m");
            yield return Wait(6f); Shot("G_grenades");
            yield return Wait(10f);
            Mark("G plays: " + PlaysOf(fg, "grenade", "wpn_grenade_fuse", "grenade_land", "vo_grenade_warning", "explosion", "rob_knockdown_land", "rob_getup", "imp_shrapnel_tink") + $"; blue grenades left {gBlue?.Grenades}");
            KillStaged(); yield return Wait(3f);

            // ---- F2: weapons at the map middle
            float f2 = Time.unscaledTime;
            int sx = mx - 70 * dx, tx = mx + 70 * dx;
            var tgtG = Squad("grunt", them, tx, my);
            var tgtJeep = Machine("vehicle", "jeep", them, tx + 10, my + 60);                                 // manned, unkillable: the snipers take its driver
            var tgtTank = Machine("vehicle", "light", them, tx - 10, my - 60, false);                          // manned, killable: destroyed with its crew
            var tough = Squad("tough", me, sx, my + 30);
            var mech = Machine("vehicle", "medium", me, sx, my - 30);
            var jeep = Machine("vehicle", "jeep", me, sx - 30 * dx, my + 70);
            var snip = Squad("sniper", me, sx, my + 80);
            var heavy = Machine("vehicle", "heavy", me, sx - 40 * dx, my - 70);
            var mlrs = Machine("vehicle", "missile_launcher", me, sx - 60 * dx, my + 10);
            var cGun = Machine("cannon", "gun", me, mx - 10, my - 110);
            var cHow = Machine("cannon", "howitzer", me, mx - 60 * dx, my - 150);
            var cMc = Machine("cannon", "missile_cannon", me, mx - 10, my + 130);
            var cGat = Machine("cannon", "gatling", me, mx + 20 * dx, my + 110);
            var light = Machine("vehicle", "light", me, sx - 20 * dx, my - 120);
            Focus(mx, my, 55f);
            Mark($"F2 weapons at ({mx >> 4},{my >> 4}): red Tough #{tough?.Id}, mech #{mech?.Id}, jeep #{jeep?.Id}, snipers #{snip?.Id}, heavy #{heavy?.Id}, MLRS #{mlrs?.Id}, gun #{cGun?.Id}, howitzer #{cHow?.Id}, missile cannon #{cMc?.Id}, gatling #{cGat?.Id}, light #{light?.Id}; blue grunts #{tgtG?.Id}, manned jeep #{tgtJeep?.Id} (unkillable), manned light tank #{tgtTank?.Id} (killable)");
            yield return Wait(0.5f);
            Attack(tough, tgtG); Attack(mech, tgtG); Attack(jeep, tgtG); Attack(snip, tgtJeep); Attack(heavy, tgtTank); Attack(mlrs, tgtG);
            Attack(cGun, tgtTank); Attack(cHow, tgtG); Attack(cMc, tgtG); Attack(cGat, tgtG);
            yield return Wait(4f); Shot("F2_weapons");
            // the red light tank drives, halts and pivots (brake / squeal / suspension)
            int lx = light != null ? light.CenterX : sx, ly = light != null ? light.CenterY : my;
            for (int leg = 0; leg < 4 && !done; leg++)
            {
                MoveTo(light, lx + (leg % 2 == 0 ? 90 : 0) * -dx, ly + (leg % 2 == 0 ? 0 : 20));
                yield return Wait(4.5f);
                if (leg == 1) { Attack(cGun, tgtG); Attack(heavy, tgtG); Attack(cGat, tgtJeep); MoveTo(jeep, sx - 120 * dx, my + 110); }   // new targets: the turrets swing again; the jeep stops firing (spin-down)
            }
            yield return Wait(8f);
            Kill(tgtTank);                                                                                      // makes sure a manned vehicle dies on screen
            yield return Wait(5f);
            Mark("F2 plays: " + PlaysOf(f2, "mech_rocket", "mech_flight", "mc_rocket", "rocket", "explosion", "wpn_tough_reload", "wpn_sniper_bolt", "wpn_jeep_spinup", "wpn_jeep_spindown", "wpn_mech_arm_spinup", "wpn_mech_arm_spindown",
                "veh_turret_slew_start", "veh_turret_slew_loop", "veh_turret_on_target", "wpn_target_lock", "wpn_breech_clank", "veh_brake", "veh_tracks_squeal", "veh_suspension", "veh_engine_shutdown", "sniped", "driverhit", "debris", "dst_fire_ignite", "exp_debris_rain"));
            Mark($"F2 blue jeep driver: {(tgtJeep != null ? (tgtJeep.Owner == Team.Neutral ? "SNIPED (jeep neutral)" : "still " + tgtJeep.Owner) : "-")}");
            KillStaged(); yield return Wait(3f);

            // ---- F3: buildings (enemy radar, our factory producing + rollout, our building destroyed, a building on fire)
            float f3 = Time.unscaledTime;
            var radar = W.Objects.Where(o => o.Type == "radar" && !o.Destroyed).OrderBy(o => D(o, mx, my)).FirstOrDefault();
            if (radar != null && radar.Owner != them) { var fl = FlagOf(radar); if (fl != null) { W.AwardZone(fl, them); Mark($"F3: radar #{radar.Id} zone awarded to {them} (AwardZone, the flag-capture path)"); } }
            if (radar != null)
            {
                var rp = radar; Focus(rp.CenterX, rp.CenterY, 55f); yield return Wait(5f); Shot("F3_enemy_radar");
                rp.SetHealth(rp.MaxHealth / 4); W.UpdateObjectHealth(rp); Mark($"F3: enemy radar #{rp.Id} damaged to 25 % (fire)");
                yield return Wait(4f);
            }
            else Mark("F3: no radar on this level");
            var prod = W.Objects.Where(o => o.IsProducer && !o.IsFort && !o.Destroyed && o.Owner == me).OrderBy(o => D(o, ourFort.CenterX, ourFort.CenterY)).FirstOrDefault();
            if (prod == null)
            {
                var cand = W.Objects.Where(o => o.IsProducer && !o.IsFort && !o.Destroyed && o.Owner != them && FlagOf(o) != null).OrderBy(o => D(o, ourFort.CenterX, ourFort.CenterY)).FirstOrDefault();
                if (cand != null) { W.AwardZone(FlagOf(cand), me); prod = cand; Mark($"F3: factory {cand.Type} #{cand.Id} zone awarded to us (AwardZone)"); }
            }
            ZObject victim = null;
            if (prod != null)
            {
                if (!prod.Building) foreach (var u in new[] { "grunt", "psycho", "tough", "sniper", "jeep", "light" }) if (prod.SetBuildingProduction(u)) break;
                prod.BFinal = W.Time + 7.0;                                                                     // harness: the unit rolls out in 7 s
                Focus(prod.CenterX, prod.CenterY + 24, 45f);
                Mark($"F3: our {prod.Type} #{prod.Id} producing {prod.CurUnit}, due in 7 s (BFinal shortened), camera 45 m");
                yield return Wait(12f); Shot("F3_factory_rollout");
                yield return Wait(4f);
                victim = W.Objects.Where(o => o.Kind == ZKind.Building && o.Owner == me && !o.IsFort && !o.IsBridge && o != prod && !o.Destroyed).OrderBy(o => D(o, prod.CenterX, prod.CenterY)).FirstOrDefault();
            }
            if (victim == null) victim = W.Objects.Where(o => o.Kind == ZKind.Building && o.Owner == me && !o.IsFort && !o.IsBridge && !o.Destroyed).OrderBy(o => D(o, ourFort.CenterX, ourFort.CenterY)).FirstOrDefault();
            if (victim != null)
            {
                Focus(victim.CenterX, victim.CenterY, 55f); yield return Wait(1.5f);
                Kill(victim); Mark($"F3: our {victim.Type} #{victim.Id} destroyed (Rachel: building destroyed)");
                yield return Wait(7f); Shot("F3_building_destroyed");
            }
            else Mark("F3: no own building to destroy");
            Mark("F3 plays: " + PlaysOf(f3, "bld_radar_loop_enemy", "bld_factory_exhaust", "bld_factory_vent", "bld_factory_accent", "ui_unit_ready", "built", "ann_building_destroyed", "dst_debris_concrete", "exp_debris_rain", "dst_fire_ignite"));

            // ---- F4: a bridge destroyed and rebuilt (repair time 9 s for the harness, 120 s in play)
            float f4 = Time.unscaledTime;
            var bridge = W.Objects.Where(o => o.IsBridge && !o.Destroyed).OrderBy(o => D(o, mx, my)).FirstOrDefault();
            if (bridge != null)
            {
                double keep = W.BridgeRepairSeconds; W.BridgeRepairSeconds = 9;
                Focus(bridge.CenterX, bridge.CenterY, 50f); yield return Wait(1.5f);
                Kill(bridge); Mark($"F4: {bridge.Type} #{bridge.Id} at ({bridge.CenterX >> 4},{bridge.CenterY >> 4}) destroyed, auto repair at +{bridge.NextAutoRepairTime - W.Time:0.0} s sim");
                W.BridgeRepairSeconds = keep;
                yield return Wait(3f); Shot("F4_bridge_down");
                yield return Wait(13f); Shot("F4_bridge_rebuilt");
                Mark($"F4: bridge destroyed = {bridge.Destroyed}");
            }
            else Mark("F4: no intact bridge");
            Mark("F4 plays: " + PlaysOf(f4, "bld_bridge_countdown", "bld_bridge_rise", "bld_bridge_drip", "exp_debris_rain", "dst_debris_concrete"));

            // ---- F5: three empty guns captured by our grunts (Rachel: gun captured, 50 % each)
            float f5 = Time.unscaledTime;
            int cx = mx - 40 * dx, cy = my + 40;
            for (int i = 0; i < 3; i++)
            {
                var sq = Squad("grunt", me, cx + i * 56, cy);
                var gun = Machine("cannon", "gatling", Team.Neutral, cx + i * 56, cy + 56, false);
                if (sq != null && gun != null) OrderOn(sq, gun);
            }
            Focus(cx + 56, cy + 30, 48f);
            yield return Wait(14f); Shot("F5_guns_captured");
            Mark("F5 plays: " + PlaysOf(f5, "ann_gun_captured", "capture", "capture_start", "enter", "engine_start"));
            KillStaged(); yield return Wait(2f);
        }

        // ================================================================ ui walk + end screen
        IEnumerator Ui()
        {
            Begin(); float u0 = Time.unscaledTime;
            var ourFort = Fort(me); var theirFort = Fort(them);
            int dx = Math.Sign(theirFort.CenterX - ourFort.CenterX), dy = Math.Sign(theirFort.CenterY - ourFort.CenterY);
            // staging: near our fort, around a grenade crate (existing, else a new one)
            int bx = ourFort.CenterX + dx * 110, by = ourFort.CenterY + dy * 90;
            var crate = W.Objects.Where(o => o.Type == "grenades" && !o.Destroyed).OrderBy(o => D(o, bx, by)).FirstOrDefault();
            if (crate == null || D(crate, bx, by) > 400)
            {
                OzCheatSpawner.FreeTile(W, bx >> 4, by >> 4, 1, true, out int ctx, out int cty);
                crate = W.CreateObject("item", "grenades", ctx * 16, cty * 16, Team.Neutral);
                try { g.SpawnUnitView(crate); } catch (Exception e) { Mark("crate view: " + e.Message); }
                Mark($"U: new grenade crate #{crate?.Id} at ({ctx},{cty})");
            }
            else { bx = crate.CenterX; by = crate.CenterY; Mark($"U: grenade crate #{crate.Id} at ({crate.CenterX >> 4},{crate.CenterY >> 4})"); }
            var A = Squad("grunt", me, bx + dx * 70, by - dy * 70); var B = Squad("grunt", me, bx + dx * 70, by - dy * 120);   // clear of the crate (a unit in front of it takes the hover)
            var crane = Machine("vehicle", "crane", me, bx - 50, by + 40);
            var gat = Machine("cannon", "gatling", me, bx - 50, by - 30);
            var empty = Machine("vehicle", "jeep", Team.Neutral, bx + dx * 100, by + dy * 60, false);                // away from the fort (bx + 110 sat on it)
            var enemy = Squad("grunt", them, bx + dx * 150, by + 80);
            var flag = W.Objects.Where(o => o.Type == "flag" && o.Owner != me).OrderBy(o => D(o, bx, by)).FirstOrDefault();
            ourFort.SetHealth(ourFort.MaxHealth * 7 / 10); W.UpdateObjectHealth(ourFort);
            var rubble = W.Objects.Where(o => o.Kind == ZKind.Building && !o.IsFort && !o.IsBridge && !o.Destroyed && o.Owner == me && !o.IsProducer).OrderBy(o => D(o, bx, by)).FirstOrDefault()
                      ?? W.Objects.Where(o => o.Kind == ZKind.Building && !o.IsFort && !o.IsBridge && !o.Destroyed && o.Owner == me && o.Type != "robot_factory").OrderBy(o => D(o, bx, by)).FirstOrDefault()
                      ?? W.Objects.Where(o => o.Kind == ZKind.Building && !o.IsFort && !o.Destroyed && (o.Owner == me || o.Owner == Team.Neutral)).OrderBy(o => D(o, bx, by)).FirstOrDefault();
            if (rubble != null) { Kill(rubble); Mark($"U: our {rubble.Type} #{rubble.Id} destroyed first (the crane's repair target)"); }
            Focus(bx + 30, by + 20, 48f);
            input.Selection.Clear();
            Mark($"U staging: grunts A #{A?.Id}, B #{B?.Id}, crane #{crane?.Id}, gatling #{gat?.Id}, empty jeep #{empty?.Id}, blue grunts #{enemy?.Id}, flag #{flag?.Id}; our fort at 70 %");
            MouseState(new Vector2(Screen.width * 0.5f, Screen.height * 0.6f), false, false);
            yield return Wait(2f); Shot("U0_staging");

            // U1 hover with nothing selected: world hover ticks
            foreach (var o in new[] { enemy, B, empty, crane }) { if (o == null) continue; yield return MoveMouse(ScreenOf(o)); yield return Wait(0.4f); }
            // U2 select A, hover targets: lock blips
            yield return Click(ScreenOf(A)); yield return Wait(0.6f);
            Mark($"U2 selection after click on A: {input.Selection.Count} ({string.Join(",", input.Selection.Select(s => s.Id))})");
            foreach (var o in new[] { enemy, empty, crate }) { if (o == null) continue; yield return MoveMouse(ScreenOf(o)); yield return Wait(0.6f); yield return MoveMouse(ScreenOfPx(bx, by + 140)); yield return Wait(0.35f); }
            // U3 right-click orders: capture a flag, enter the empty jeep, pick up grenades (the mouse is on the object: PlayerInput.Hover)
            string Last(ZObject u) => u.Waypoints.Count > 0 ? u.Waypoints[u.Waypoints.Count - 1].mode + (u.Waypoints[u.Waypoints.Count - 1].refId >= 0 ? " ref " + u.Waypoints[u.Waypoints.Count - 1].refId : "") : "none";
            // enter first (A still stands by the crate), then the flag (the order must really name the flag), the pickup last (A walks there)
            Focus(empty.CenterX, empty.CenterY, 44f); yield return F(3);
            yield return HoverOn(empty); yield return Click(mousePos, true); yield return Wait(1.2f); Mark($"U3 enter order (hover ok {hoverOk}); A waypoint {Last(A)}");
            foreach (var fl in W.Objects.Where(o => o.Type == "flag" && o.Owner != me).OrderBy(o => D(o, bx, by)).Take(6).ToList())
            {
                Focus(fl.CenterX, fl.CenterY, 44f); yield return F(3);
                yield return HoverOn(fl);
                if (!hoverOk) { Mark($"U3 flag #{fl.Id}: the mouse never hovers it (a building / unit in front), next flag"); continue; }
                yield return Click(mousePos, true); yield return Wait(1.2f);
                bool named = A.Waypoints.Count > 0 && A.Waypoints[A.Waypoints.Count - 1].refId == fl.Id;
                Mark($"U3 flag #{fl.Id} order; A waypoint {Last(A)}"); if (named) break;
            }
            Focus(crate.CenterX, crate.CenterY, 40f); yield return F(3);
            yield return HoverOn(crate); yield return Click(mousePos, true); yield return Wait(1.2f); Mark($"U3 pickup order (hover ok {hoverOk}); A waypoint {Last(A)}");
            Focus(bx + 30, by + 20, 48f); yield return F(3);
            // U4 crane: hover an enemy (nono), right-click a destroyed building of ours (crane repair)
            yield return Click(ScreenOf(crane)); yield return Wait(0.5f);
            yield return MoveMouse(ScreenOf(enemy)); yield return Wait(0.7f);
            if (rubble != null)
            {
                Focus(rubble.CenterX, rubble.CenterY, 55f); yield return F(3);
                yield return HoverOn(rubble); yield return Click(mousePos, true); yield return Wait(1.2f);
                Mark($"U4 crane order on the rubble of {rubble.Type} #{rubble.Id} (hover ok {hoverOk}); crane waypoint {Last(crane)}");
            }
            else Mark("U4 no destroyed building for the crane");
            Focus(bx + 30, by + 20, 48f); yield return F(3);
            // U5 EXIT CREW on the gatling (E)
            yield return Click(ScreenOf(gat)); yield return Wait(0.5f);
            yield return Tap(Key.E); yield return Wait(1.2f); Mark($"U5 gatling crew after E: drivers {gat.Drivers.Count}, owner {gat.Owner}");
            // U6 Shift add / remove, double-click select-all
            yield return Click(ScreenOf(A)); yield return Wait(0.4f);
            Keys(Key.LeftShift); yield return F(3);
            yield return Click(ScreenOf(B)); yield return Wait(0.4f);
            Mark($"U6 after Shift+click B: {input.Selection.Count}");
            yield return Click(ScreenOf(B)); yield return Wait(0.4f);
            Mark($"U6 after Shift+click B again: {input.Selection.Count}");
            Keys(); yield return F(3); yield return Wait(0.4f);
            var pa = ScreenOf(A); yield return MoveMouse(pa);
            MouseState(pa, true, false); yield return F(2); MouseState(pa, false, false); yield return F(3);
            MouseState(pa, true, false); yield return F(2); MouseState(pa, false, false); yield return F(4);
            yield return Wait(0.5f); Mark($"U6 after double-click A: {input.Selection.Count}");
            // U7 HUD group buttons: Ctrl+click store 1, click recall 1, click empty 5
            var gb = Field<HudButton[]>(hud, "groupBtn");
            if (gb != null && gb[1] != null && gb[5] != null)
            {
                Keys(Key.LeftCtrl); yield return F(3); yield return Click(Centre(gb[1].Rt)); Keys(); yield return F(3); yield return Wait(0.5f);
                yield return Click(ScreenOf(crane)); yield return Wait(0.4f);
                yield return Click(Centre(gb[1].Rt)); yield return Wait(0.6f);
                yield return Click(Centre(gb[5].Rt)); yield return Wait(0.6f);
                Mark($"U7 group 1 recall -> selection {input.Selection.Count}");
            }
            else Mark("U7 no group buttons found");
            // U8 rally tool on our fort / factory: arm, Shift chain 2 points, release, arm + Esc, CLEAR; a greyed button
            var prodB = W.Objects.Where(o => o.IsProducer && o.Owner == me && !o.Destroyed).OrderBy(o => D(o, bx, by)).FirstOrDefault();
            input.Selection.Clear(); yield return F(2);
            if (prodB != null && hud.Production != null)
            {
                Focus(prodB.CenterX, prodB.CenterY + 40, 55f); yield return F(4);
                yield return Click(ScreenOfPx(prodB.CenterX, prodB.CenterY)); yield return Wait(0.8f);
                if (hud.Production.Building != prodB) { hud.Production.Open(prodB); Mark("U8 production window opened by call (the click did not open it)"); yield return Wait(0.6f); }
                else Mark("U8 production window opened by a click on " + prodB.Type);
                var bRally = Field<HudButton>(hud.Production, "bRally"); var bClear = Field<HudButton>(hud.Production, "bClear");
                if (bRally != null)
                {
                    yield return Click(Centre(bRally.Rt)); yield return Wait(0.5f); Mark($"U8 rally armed {hud.Rally.Armed}");
                    Keys(Key.LeftShift); yield return F(3);
                    yield return Click(ScreenOfPx(prodB.CenterX + 60, prodB.CenterY + 100)); yield return Wait(0.3f);
                    yield return Click(ScreenOfPx(prodB.CenterX + 120, prodB.CenterY + 110)); yield return Wait(0.3f);
                    Keys(); yield return F(4); yield return Wait(0.6f); Mark($"U8 rally points {prodB.RallyPoints.Count}");
                    if (bClear != null) { yield return Click(Centre(bClear.Rt)); yield return Wait(0.6f); Mark($"U8 after CLEAR rally points {prodB.RallyPoints.Count}"); }
                    yield return Click(Centre(bRally.Rt)); yield return Wait(0.4f);
                    yield return Tap(Key.Escape); yield return Wait(0.5f); Mark($"U8 after Esc armed {hud.Rally.Armed}");
                    var pm0 = FindFirstObjectByType<OpenZPauseMenu>(); if (pm0 && pm0.IsOpen) { pm0.Close(); Mark("U8 Esc also opened the pause menu: closed"); yield return Wait(0.4f); }
                    if (hud.Production.Building != prodB) { hud.Production.Open(prodB); yield return Wait(0.5f); }
                }
                var all = typeof(OpenZHud).GetField("buttons", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as List<HudButton>;
                var grey = all?.FirstOrDefault(b => b != null && b.Rt && b.Rt.gameObject.activeInHierarchy && !b.Interactable && b.OnClick != null
                                                   && RectTransformUtility.RectangleContainsScreenPoint(b.Rt, Centre(b.Rt), null) && Centre(b.Rt).x > 0 && Centre(b.Rt).y > 0);
                if (grey != null) { yield return Click(Centre(grey.Rt)); yield return Wait(0.5f); Mark($"U8 clicked the greyed button '{grey.Rt.name}'"); }
                else Mark("U8 no greyed button on screen");
                if (hud.Production.Building != null) hud.Production.Close();
                yield return Wait(0.4f);
            }
            else Mark("U8 no own producer");
            // U9 P cheat: hover tiles, drag a red grunt tile to the map, drop a blue tile back on the panel
            yield return Tap(Key.P); yield return Wait(0.8f);
            var redCol = hud.Root.Find("CheatRed"); var blueCol = hud.Root.Find("CheatBlue");
            if (redCol && blueCol)
            {
                var t1 = redCol.Find("Cell_grunt") as RectTransform; var t2 = blueCol.Find("Cell_psycho") as RectTransform; var t3 = redCol.Find("Cell_sniper") as RectTransform;
                foreach (var t in new[] { t1, t3, t2 }) if (t) { yield return MoveMouse(Centre(t)); yield return Wait(0.3f); }
                if (t1)
                {
                    Vector2 c = Centre(t1), drop = ScreenOfPx(bx + 20, by + 120);
                    yield return MoveMouse(c); MouseState(c, true, false); yield return F(3);
                    for (int k = 1; k <= 8; k++) { MouseState(Vector2.Lerp(c, drop, k / 8f), true, false); yield return F(1); }
                    MouseState(drop, true, false); yield return F(2); MouseState(drop, false, false); yield return F(4);
                    yield return Wait(1f); Mark("U9 dragged a red grunt onto the map");
                }
                if (t2 && t1)
                {
                    Vector2 c = Centre(t2), drop = Centre(t1);
                    yield return MoveMouse(c); MouseState(c, true, false); yield return F(3);
                    for (int k = 1; k <= 8; k++) { MouseState(Vector2.Lerp(c, drop, k / 8f), true, false); yield return F(1); }
                    MouseState(drop, true, false); yield return F(2); MouseState(drop, false, false); yield return F(4);
                    yield return Wait(0.6f); Mark("U9 dropped a blue psycho tile back on the panel");
                }
            }
            else Mark($"U9 cheat columns not found (DevEnabled {OzCheatSpawner.DevEnabled})");
            yield return Tap(Key.P); yield return Wait(0.6f);
            // U10 pause menu EFFECTS / VOICE sliders: press on the thumb, release (value unchanged)
            var pm = FindFirstObjectByType<OpenZPauseMenu>();
            if (pm)
            {
                v0Effects = PlayerPrefs.GetFloat("oz_vol_effects", -1f); v0Voice = PlayerPrefs.GetFloat("oz_vol_voice", -1f);
                pm.Open(); yield return Wait(0.8f);
                var sl = pm.GetType().GetField("sliders", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(pm) as System.Collections.IList;
                if (sl != null)
                    foreach (var s in sl)
                    {
                        string key = (string)s.GetType().GetField("key").GetValue(s); if (key != "oz_vol_effects" && key != "oz_vol_voice") continue;
                        var tr = (RectTransform)s.GetType().GetField("track").GetValue(s); float v = (float)s.GetType().GetField("v").GetValue(s);
                        var r = tr.rect; var p = RectTransformUtility.WorldToScreenPoint(null, tr.TransformPoint(new Vector3(r.xMin + v * r.width, r.center.y, 0f)));
                        yield return MoveMouse(p); MouseState(p, true, false); yield return F(3); MouseState(p + Vector2.right * 0.2f, true, false); yield return F(2);
                        MouseState(p, false, false); yield return F(4); yield return Wait(1.2f);
                        Mark($"U10 slider {key} pressed + released at v {v:0.00}");
                    }
                pm.Close(); yield return Wait(0.6f);
            }
            else Mark("U10 no pause menu");
            Mark("U walk plays: " + PlaysOf(u0, "ui_world_hover", "ui_target_lock", "ui_target_nono", "ui_order_capture", "ui_order_enter", "ui_order_pickup", "ui_order_repair", "ui_order_exit",
                "ui_deselect", "ui_select_all", "ui_group_store", "ui_group_recall", "ui_select_empty", "ui_rally_arm", "ui_rally_point", "ui_cancel", "ui_rally_clear", "ui_disabled", "ui_drag_pick", "ui_drag_cancel", "rob_land", "ui_preview_sfx", "ui_preview_voice"));
            yield return Wait(4f);                                                                               // grunts A reach the crate (pickup)
            Mark("U pickup: " + PlaysOf(u0, "pickup") + $"; A grenades {A.Grenades}");
            // U11 the end: L02 WIN, L17 LOSS, then the end screen reveal
            bool win = level == "L02";
            float e0 = Time.unscaledTime;
            Mark($"U11 DebugFortFalls({win}): " + OpenZVictory.DebugFortFalls(win));
            float wEnd = T; while (T - wEnd < 60f && Plays("ui_chart_draw", e0) == 0 && !done) { lastMark = T; yield return Wait(0.5f); }
            yield return Wait(4f); Shot(win ? "U11_victory_endscreen" : "U11_defeat_endscreen");
            Mark("U11 plays: " + PlaysOf(e0, "ui_victory_hit", "ui_defeat_hit", "ui_band_whoosh", "ui_plate_land", "ui_chart_draw"));
        }

        void Finish(string why)
        {
            if (done) return; done = true;
            try
            {
                Mark(why == null ? "DONE" : "STOPPED: " + why);
                if (part != "cov" && AudioDirector.I)
                {
                    Pull();
                    SoundV3Proof.Report(Path.Combine(Dir, "sound_v3_report_ring.md"));                        // the package's report (ring-limited, log only)
                    File.WriteAllText(Path.Combine(Dir, "sound_v3_report.md"), FullReport());
                    File.WriteAllText(Path.Combine(Dir, "ozsoundlog.txt"), OzSoundLog.Format(full));
                }
                if (v0Effects >= 0f) PlayerPrefs.SetFloat("oz_vol_effects", v0Effects);
                if (v0Voice >= 0f) PlayerPrefs.SetFloat("oz_vol_voice", v0Voice);
                log.AppendLine($"errors/exceptions in the Console during the run: {errors}; GPU peak (GpuGuard) {peak} C");
                log.AppendLine("## VERDICTS"); foreach (var v in verdicts) log.AppendLine("  " + v);
                File.WriteAllText(Path.Combine(Dir, "proof_log.txt"), log.ToString());
                PlayerPrefs.SetInt("oz_testmode", 0); PlayerPrefs.SetInt("oz_music", 1); PlayerPrefs.Save();
            }
            catch (Exception e) { Debug.LogError("[sv3proof] finish: " + e); }
            if (part == "ui" && why == null && AudioDirector.I) StartCoroutine(SweepThenStop());
            else UnityEditor.EditorApplication.isPlaying = false;
        }

        /// <summary>SoundV3Proof.Report's table over this run's COMPLETE log, plus the director's own counters (loops and voices never
        /// reach OzSoundLog: StartLoop / PlayVoice count into AudioDirector.Plays) and CombatAudio.PlayCounts since Begin.</summary>
        string FullReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# sound_v3 Play proof ({level} {part}), complete log: {full.Count} entries, {(full.Count > 0 ? full[full.Count - 1].t - beginT : 0f):0} s\n");
            sb.AppendLine("| id / key | log plays | director plays (loops, voices) | CombatAudio plays | drops (reason) | vol max | vol median | heard |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            var silent = new List<string>(); var ca = CombatAudio.I;
            foreach (var id in SoundV3Proof.NewEvents.Concat(SoundV3Proof.NewKeys))
            {
                var mine = full.Where(e => e.key == id).ToList();
                var plays = mine.Where(e => e.outcome != null && e.outcome.StartsWith("PLAY")).ToList();
                var drops = mine.Where(e => e.outcome != null && e.outcome.StartsWith("DROP")).GroupBy(e => e.outcome).Select(gr => $"{gr.Key.Substring(5)} x{gr.Count()}");
                int adp = AdPlays(id), cap = 0;
                if (ca && SoundV3Proof.NewKeys.Contains(id)) cap = (ca.PlayCounts.TryGetValue(id, out var c1) ? c1 : 0) - (caBase.TryGetValue(id, out var c0) ? c0 : 0);
                float max = plays.Count > 0 ? plays.Max(e => e.vol) : 0f;
                float med = plays.Count > 0 ? plays.Select(e => e.vol).OrderBy(v => v).ElementAt(plays.Count / 2) : 0f;
                bool heard = plays.Count > 0 || adp > 0 || cap > 0;
                sb.AppendLine($"| {id} | {plays.Count} | {adp} | {cap} | {string.Join(", ", drops)} | {max:0.000} | {med:0.000} | {(heard ? "yes" : "NO")} |");
                if (!heard) silent.Add(id);
            }
            var dup = new List<string>(); var ps = full.Where(e => e.outcome != null && e.outcome.StartsWith("PLAY")).ToList();
            for (int i = 1; i < ps.Count; i++)
                for (int j = i - 1; j >= 0 && ps[i].t - ps[j].t < 0.03f; j--)
                    if (ps[i].key == ps[j].key && ps[i].clip == ps[j].clip && Mathf.Abs(ps[i].dist - ps[j].dist) < 0.5f)
                        dup.Add($"{ps[i].t:0.00} {ps[i].key} {ps[i].clip}");
            sb.AppendLine($"\nnever played in this run ({silent.Count}): {string.Join(", ", silent)}");
            sb.AppendLine($"duplicates ({dup.Count}): {string.Join("; ", dup.Take(40))}");
            if (ad) sb.AppendLine("\n## AudioDirector coverage (this run)\n\n" + ad.CoverageReport());
            return sb.ToString();
        }

        IEnumerator SweepThenStop()
        {
            Debug.Log("[sv3proof] coverage sweep");
            yield return StartCoroutine(AudioDirector.I.CoverageSweep());
            try { File.WriteAllText(Path.Combine(Dir, "coverage_after_sweep.md"), AudioDirector.I.CoverageReport()); } catch (Exception e) { Debug.LogWarning("[sv3proof] " + e.Message); }
            UnityEditor.EditorApplication.isPlaying = false;
        }
    }
}
#endif
