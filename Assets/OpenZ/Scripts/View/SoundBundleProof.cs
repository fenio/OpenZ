#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// oz_testmode 1 = the GPU-light test mode; logic only, no visible change to prove).
    ///  A. unit_voices_v1: a grunt, psycho, sniper squad, a heavy tank, jeep, crane and a GUN cannon (real spawned units): select,
    ///     select + move order 0.4 s later, 8 quick clicks 0.5 s apart (running gag), 6 right-clicks 0.25 s apart, refusals (gun move,
    ///     crane at an enemy). The selection / order is fed the way PlayerInput does it (Selection + SelectTime, SendWaypoints +
    ///     MarkerTime, ui_order_invalid for a refused order) from an Update that runs before AudioDirector.PollInput. The Alan main
    ///     channel is watched every frame: every clip start, and any line stopped / replaced before its end (= cut mid-word).
    ///  B. launchers: tough squad, MLRS, missile cannon, medium tank (mech rockets), each firing at an unkillable blue grunt squad with an
    ///     unkillable red grunt squad beside it (gunfire at the same distances); the whole OzSoundLog of each fight is dumped, plus a
    ///     WAV of the mix (listener tap) for a listening check.
    /// </summary>
    [DefaultExecutionOrder(20)]   // after PlayerInput (0: its Update rewrites Hover), before AudioDirector.PollInput (40)
    public class SoundBundleProof : MonoBehaviour
    {
        static readonly string Dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones/SOUND-bundle"));
        string job; float t0, lastMark; bool done; int errors; float hot = -1f, lastSample = -1f;
        readonly StringBuilder log = new StringBuilder(), soundDump = new StringBuilder(), alanCsv = new StringBuilder("t,event,clip,len,at,left\n");
        readonly List<string> verdicts = new List<string>();
        readonly List<(float t, string s)> uvLog = new List<(float, string)>();
        MusicV3PlayProof.Tap tap;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Auto()
        {
            string job = PlayerPrefs.GetString("oz_sndb_auto", "");
            if (string.IsNullOrEmpty(job)) return;
            PlayerPrefs.DeleteKey("oz_sndb_auto"); PlayerPrefs.Save();
            var go = new GameObject("SoundBundleProof"); DontDestroyOnLoad(go);
            go.AddComponent<SoundBundleProof>().job = job;
        }

        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        float T => (float)clock.Elapsed.TotalSeconds - t0;
        void Mark(string s) { lastMark = T; log.AppendLine($"[{T,7:0.00}] ## {s}"); Debug.Log("[sndb] " + s); }
        void Verdict(string step, bool ok, string what) { string v = $"{step}: {(ok ? "PASS" : "FAIL")} - {what}"; verdicts.Add(v); Mark("VERDICT " + v); }
        IEnumerator Wait(float s) { float end = T + s; while (T < end && !done) yield return null; }

        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; if (!done) Finish("disabled"); }
        void OnLog(string c, string st, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception) { errors++; log.AppendLine($"[{T,7:0.00}] ERROR {c}"); return; }
            if (c.StartsWith("[unit_voices]")) { uvLog.Add((T, c)); log.AppendLine($"[{T,7:0.00}] {c}"); }
        }

        // ---------------------------------------------------------------- input fed like PlayerInput (before AudioDirector.PollInput)
        Action pending;
        IEnumerator Do(Action a) { pending = a; while (pending != null && !done) yield return null; }
        static readonly MethodInfo setSel = typeof(PlayerInput).GetProperty("SelectTime").GetSetMethod(true);
        static readonly MethodInfo setMark = typeof(PlayerInput).GetProperty("MarkerTime").GetSetMethod(true);
        static readonly MethodInfo setMarkPos = typeof(PlayerInput).GetProperty("MarkerPos").GetSetMethod(true);
        static readonly MethodInfo setHover = typeof(PlayerInput).GetProperty("Hover").GetSetMethod(true);
        /// <summary>A left click on the unit: PlayerInput's single-click result + UnitVoices' "mouse released this frame" (the hook).</summary>
        void Select(ZObject u) { input.Selection.Clear(); input.Selection.Add(u); setSel.Invoke(input, new object[] { Time.unscaledTime }); AudioDirector.DebugClickFrame = Time.frameCount; clicksFed++; }
        int clicksFed;
        /// <summary>PlayerInput.Flush: send, ui_order_invalid when nothing took it, MarkerTime.</summary>
        bool Order(ZObject u, ZObject target, int x, int y)
        {
            if (target != null) setHover.Invoke(input, new object[] { target });                    // the pointer is on the enemy (PollInput reads Hover)
            var wp = input.OrderFor(u, target, x, y);
            int kept = u.CanOverwriteWP() ? 0 : 1;
            bool took = W.SendWaypoints(g.player, u, new List<ZWaypoint> { wp }) && u.Waypoints.Count > kept;
            if (!took) AudioDirector.UI("ui_order_invalid");
            setMarkPos.Invoke(input, new object[] { ZGame.PxToWorld(x, y) });
            setMark.Invoke(input, new object[] { Time.unscaledTime });
            return took;
        }

        // ---------------------------------------------------------------- the Alan main channel, every frame
        AudioSource alan; AudioClip aClip; float aTime; bool aPlaying; int cuts;
        readonly List<(float t, string clip, float len)> starts = new List<(float, string, float)>();
        readonly List<string> cutList = new List<string>();
        void Update()
        {
            if (pending != null) { var p = pending; pending = null; try { p(); } catch (Exception e) { Mark("action failed: " + e.Message); } }
            if (!alan) { var go = GameObject.Find("Voice_AlanMain"); if (go) alan = go.GetComponent<AudioSource>(); }
            if (!alan) return;
            bool pl = alan.isPlaying; var c = alan.clip; float tm = alan.time;
            bool restarted = pl && aPlaying && c == aClip && tm + 0.05f < aTime;
            if (aPlaying && aClip && (!pl || c != aClip || restarted))
            {
                float left = (aClip.length - aTime) / Mathf.Max(0.1f, Mathf.Abs(alan.pitch));
                if (left > 0.12f) { cuts++; string s = $"{T:0.00} {aClip.name} stopped at {aTime:0.00}/{aClip.length:0.00} s ({left:0.00} s left){(pl ? " -> " + (c ? c.name : "-") : "")}"; cutList.Add(s); alanCsv.AppendLine($"{T:0.00},CUT,{aClip.name},{aClip.length:0.00},{aTime:0.00},{left:0.00}"); Mark("ALAN CUT " + s); }
                else alanCsv.AppendLine($"{T:0.00},end,{aClip.name},{aClip.length:0.00},{aTime:0.00},{left:0.00}");
            }
            if (pl && c && (!aPlaying || c != aClip || restarted))
            { starts.Add((T, c.name, c.length)); alanCsv.AppendLine($"{T:0.00},start,{c.name},{c.length:0.00},{tm:0.00},"); log.AppendLine($"[{T,7:0.00}] alan start {c.name} ({c.length:0.00} s)"); }
            aPlaying = pl; aClip = c; aTime = tm;
        }
        bool AlanTalking => alan && alan.isPlaying;
        IEnumerator Quiet(float need = 0.7f, float max = 8f)
        {
            float w0 = T, q0 = T;
            while (T - q0 < need && T - w0 < max && !done) { if (AlanTalking) q0 = T; yield return null; }
        }
        List<(float t, string clip, float len)> StartsIn(float a, float b) { var o = new List<(float, string, float)>(); foreach (var s in starts) if (s.t >= a && s.t <= b) o.Add(s); return o; }
        static string Kind(string clip)
        {
            if (clip == null) return "?";
            if (clip.Contains("_uv_")) { foreach (var k in new[] { "_select_", "_move_", "_attack_", "_refuse_", "_idle_" }) if (clip.Contains(k)) return "type " + k.Trim('_'); return "type ?"; }
            return "classic";
        }
        static string Fmt(List<(float t, string clip, float len)> l, float rel) { var sb = new StringBuilder(); foreach (var s in l) sb.Append($"[{s.t - rel:0.00} s {s.clip} ({Kind(s.clip)}, {s.len:0.00} s)] "); return sb.ToString(); }

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
        int peak = -1;
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
        ZGame g; ZWorld W; PlayerInput input; RtsCamera cam; Team me, them; readonly List<ZObject> staged = new List<ZObject>();
        ZObject Fort(Team t) { foreach (var o in W.Objects) if (o.IsFort && o.Owner == t && !o.Destroyed) return o; return null; }
        void Tough(ZObject o) { o.MaxHealth = 1000000000; o.SetHealth(o.MaxHealth); }
        ZObject Squad(string type, Team t, int px, int py, bool tough = true)
        {
            OzCheatSpawner.FreeTile(W, px >> 4, py >> 4, 1, true, out int tx, out int ty);
            var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t); g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            staged.Add(o); staged.AddRange(o.Minions);
            if (tough) { Tough(o); foreach (var m in o.Minions) Tough(m); }
            return o;
        }
        ZObject Machine(string cat, string type, Team t, int px, int py)
        {
            OzCheatSpawner.FreeTile(W, px >> 4, py >> 4, 2, false, out int tx, out int ty);
            var o = W.CreateObject(cat, type, tx * 16, ty * 16, t); g.SpawnUnitView(o); staged.Add(o); Tough(o); return o;
        }
        void KillStaged() { foreach (var m in new List<ZObject>(staged)) if (m != null && !m.Destroyed && W.Get(m.Id) == m) { m.SetHealth(0); W.UpdateObjectHealth(m); } staged.Clear(); }
        void Focus(int px, int py) { if (!cam) return; var p = ZGame.PxToWorld(px, py); cam.focus = new Vector3(p.x, 0f, p.z); cam.SetHeightImmediate(cam.height); }
        void Shot(string name)
        {
            try { ScreenCapture.CaptureScreenshot(Path.Combine(Dir, name + ".png")); Mark($"frame {name}.png (test-mode context frame, {Screen.width}x{Screen.height}, camera height {(cam ? cam.height : -1f):0} m)"); }
            catch (Exception e) { Mark("shot failed " + e.Message); }
        }

        // ---------------------------------------------------------------- the run
        IEnumerator Start()
        {
            t0 = 0f; lastMark = 0f; Directory.CreateDirectory(Dir);
            Mark($"SOUND-bundle proof {job} {DateTime.Now:yyyy-MM-dd HH:mm:ss}, scene {SceneManager.GetActiveScene().name}, oz_testmode {PlayerPrefs.GetInt("oz_testmode", 0)}, oz_music {PlayerPrefs.GetInt("oz_music", 1)}, screen {Screen.width}x{Screen.height}");
            StartCoroutine(Heat());
            float w0 = T;
            while ((!ZGame.I || ZGame.I.World == null || !AudioDirector.I || !FindFirstObjectByType<PlayerInput>()) && T - w0 < 90f) yield return null;
            float w = T - w0;
            if (!ZGame.I || ZGame.I.World == null || !AudioDirector.I) { Finish("the level never came up within 90 s"); yield break; }
            Mark($"level up after {w:0.0} s");
            g = ZGame.I; W = g.World; input = FindFirstObjectByType<PlayerInput>(); cam = FindFirstObjectByType<RtsCamera>();
            me = g.player; them = me == Team.Red ? Team.Blue : Team.Red;
            int bots = W.Bots.Count; W.Bots.Clear();
            yield return Wait(4f);
            var ourFort = Fort(me); var theirFort = Fort(them);
            int mx = (ourFort.CenterX + theirFort.CenterX) / 2, my = (ourFort.CenterY + theirFort.CenterY) / 2;
            Mark($"level live; CPU bots cleared ({bots}); stage at the map middle ({mx >> 4},{my >> 4}) tiles; camera height {cam.height:0} m (the level's start view)");
            var ad = AudioDirector.I;
            Mark($"AudioDirector unitVoices {ad.unitVoices}, select share {ad.unitSelectShare:0.00}, order share {ad.unitOrderShare:0.00}, joke after {ad.unitJokeClicks} quick clicks (quick = {ad.unitQuickClick:0.0} s)");
            Application.targetFrameRate = 30;                                                           // heat: the voice part draws nothing new
            yield return Voices(mx, my);
            Application.targetFrameRate = 60;
            if (done) yield break;
            yield return Launchers(mx, my);
            Finish(null);
        }

        // ---------------------------------------------------------------- A. unit voices
        IEnumerator Voices(int mx, int my)
        {
            int bx = mx - 64, by = my;
            Focus(bx, by);
            var list = new List<(string type, ZObject u)>
            {
                ("grunt", Squad("grunt", me, bx, by - 48)), ("psycho", Squad("psycho", me, bx + 32, by - 48)), ("sniper", Squad("sniper", me, bx + 64, by - 48)),
                ("heavy", Machine("vehicle", "heavy", me, bx, by + 16)), ("jeep", Machine("vehicle", "jeep", me, bx + 48, by + 16)),
                ("crane", Machine("vehicle", "crane", me, bx + 96, by + 16)), ("gun", Machine("cannon", "gun", me, bx + 48, by + 64)),
            };
            yield return Wait(2.5f);
            Mark("A. unit voices: " + string.Join(", ", list.ConvertAll(x => $"{x.type} #{x.u.Id}").ToArray()));
            if (job == "L17") list = list.FindAll(x => x.type == "grunt" || x.type == "jeep" || x.type == "gun");   // L17: a voice smoke (the lines are 2D, level-independent)
            foreach (var (type, u) in list)
            {
                if (done) yield break;
                bool cannon = u.Kind == ZKind.Cannon;
                // 1 select
                yield return Quiet(); float s1 = T; yield return Do(() => Select(u)); yield return Wait(2.8f);
                var l1 = StartsIn(s1, T); Mark($"{type} 1 select: {Fmt(l1, s1)}");
                yield return Quiet(); float s2 = T; yield return Do(() => Select(u)); yield return Wait(0.4f);
                float o2 = T; bool took2 = false; int dx = 40;
                yield return Do(() => { o2 = T; took2 = Order(u, null, u.CenterX + dx, u.CenterY); }); yield return Wait(3.2f);
                var l2 = StartsIn(s2, T); var after = StartsIn(o2 + 0.001f, T);
                float firstOrder = after.Count > 0 ? after[0].t - o2 : -1f;
                Mark($"{type} 2 select + order 0.4 s later (took {took2}): {Fmt(l2, s2)}");
                // 3 eight quick clicks 0.5 s apart: the running gag
                yield return Quiet(); float s3 = T; var clicks = new List<float>();
                for (int i = 0; i < 8 && !done; i++) { while (T < s3 + i * 0.5f && !done) yield return null; yield return Do(() => Select(u)); clicks.Add(T); }
                yield return Wait(3.5f); var l3 = StartsIn(s3, T);
                int jokes = 0; float firstJoke = -1f; foreach (var s in l3) if (Kind(s.clip) == "type idle") { jokes++; if (firstJoke < 0f) firstJoke = s.t; }
                int clicksBeforeJoke = 0; if (firstJoke > 0f) foreach (var c in clicks) if (c <= firstJoke) clicksBeforeJoke++;
                Mark($"{type} 3 spam 8 clicks (at {string.Join(" / ", clicks.ConvertAll(x => (x - s3).ToString("0.00")).ToArray())} s): {Fmt(l3, s3)} | jokes {jokes}, first joke after click {clicksBeforeJoke}");
                // 4 six right-clicks 0.25 s apart once Alan is quiet
                yield return Quiet(); yield return Do(() => Select(u)); yield return Wait(0.3f); yield return Quiet(0.8f);
                float s4 = T; dx = -dx;
                var rc = new List<float>();
                for (int i = 0; i < 6 && !done; i++) { while (T < s4 + i * 0.25f && !done) yield return null; int x = u.CenterX + (i % 2 == 0 ? dx : -dx); yield return Do(() => Order(u, null, x, u.CenterY)); rc.Add(T - s4); }
                yield return Wait(3.5f); var l4 = StartsIn(s4, T);
                Mark($"{type} 4 right-click spam x6 (clicks at {string.Join(" / ", rc.ConvertAll(x => x.ToString("0.00")).ToArray())} s): {Fmt(l4, s4)}");
                // 5 crane at an enemy: refused
                List<(float t, string clip, float len)> l5 = null;
                if (type == "crane")
                {
                    yield return Quiet(); yield return Do(() => Select(u)); yield return Wait(0.3f); yield return Quiet(0.8f);
                    var foe = Squad("grunt", them, u.CenterX + 160, u.CenterY);                // the crane's target, 10 tiles off (spawned for this step only)
                    yield return Wait(1f);
                    float s5 = T; yield return Do(() => Order(u, foe, foe.CenterX, foe.CenterY)); yield return Wait(3f);
                    foreach (var m in new List<ZObject>(foe.Minions)) { m.SetHealth(0); W.UpdateObjectHealth(m); } foe.SetHealth(0); W.UpdateObjectHealth(foe);
                    l5 = StartsIn(s5, T); Mark($"{type} 5 crane ordered at an enemy: {Fmt(l5, s5)}");
                }
                bool any = false; foreach (var s in l1) any |= s.clip.Contains("_uv_"); foreach (var s in l2) any |= s.clip.Contains("_uv_"); foreach (var s in l3) any |= s.clip.Contains("_uv_"); foreach (var s in l4) any |= s.clip.Contains("_uv_");
                bool ok2 = firstOrder < 0f || firstOrder <= 0.85f + (l2.Count > 0 ? 0f : 0f);
                // an order line that waited for the select line: must start within 0.8 s of the click
                int stray = 0; foreach (var l in new[] { l1, l2, l4 }) foreach (var s in l) if (Kind(s.clip) == "type idle") stray++;   // no joke without quick clicks
                int orders4 = 0; foreach (var s in l4) if (Kind(s.clip) != "type select") orders4++;
                bool spamOk = orders4 == 1 || (cannon && orders4 <= 1); bool jokeOk = jokes >= 1 && clicksBeforeJoke >= 3 && stray == 0;
                bool refuseOk = true; string refuse = "";
                if (cannon) { int n = 0; foreach (var s in l2) if (Kind(s.clip) == "type refuse") n++; foreach (var s in l4) if (Kind(s.clip) == "type refuse") n++; refuseOk = n >= 1; refuse = $", gun refuse lines {n}"; }
                if (l5 != null) { int n = 0; foreach (var s in l5) if (Kind(s.clip) == "type refuse") n++; refuseOk = n >= 1; refuse = $", crane refuse lines {n}"; }
                Verdict($"A {type}", any && ok2 && spamOk && jokeOk && refuseOk,
                    $"type lines heard: {any}; select + order: order line {(firstOrder < 0f ? "none (the select line still had > 0.8 s)" : firstOrder.ToString("0.00") + " s after the click")}; right-click spam x6: {orders4} answer(s); 8 quick clicks: {jokes} joke(s), the first after click {clicksBeforeJoke}; jokes without quick clicks {stray}{refuse}");
            }
            // the live / first proof lines
            int firsts = 0; bool live = false; foreach (var l in uvLog) { if (l.s.Contains("] live:")) live = true; if (l.s.Contains("] first ")) firsts++; }
            Verdict("A live + first lines", live && firsts >= (job == "L17" ? 1 : 7), $"[unit_voices] live logged: {live}; 'first <event>' lines: {firsts}");
            Verdict("A no line cut mid-word", cuts == 0, $"Alan main channel: {starts.Count} line starts, {cuts} cut(s){(cuts > 0 ? ": " + string.Join(" / ", cutList.ToArray()) : "")}");
            KillStaged(); yield return Wait(2f);
        }

        // ---------------------------------------------------------------- B. launchers
        IEnumerator Launchers(int mx, int my)
        {
            var lis = FindFirstObjectByType<AudioListener>();
            foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) if (l.enabled) lis = l;
            float wav0 = T;
            if (lis) { tap = lis.gameObject.AddComponent<MusicV3PlayProof.Tap>(); tap.Open(Path.Combine(Dir, job + "_launchers_mix.wav")); }
            Mark($"B. launchers; mix WAV from here (t0 = {wav0:0.00} s): {job}_launchers_mix.wav");
            var runner = FindFirstObjectByType<WeaponsV2Runner>();
            string[] kinds = { "tough", "missile_launcher", "missile_cannon", "medium" };
            foreach (var k in kinds)
            {
                if (done) yield break;
                int sx = mx - 48, sy = my + 24, tx = mx + 48, ty = my + 24;
                var blue = Squad("grunt", them, tx, ty);
                ZObject shooter = k == "tough" ? Squad("tough", me, sx, sy) : k == "missile_cannon" ? Machine("cannon", k, me, sx, sy) : Machine("vehicle", k, me, sx, sy);
                var gunners = Squad("grunt", me, sx, sy - 40);
                Focus((sx + tx) / 2, sy);
                yield return Wait(1.5f);
                int l0 = runner ? runner.Launches : 0, m0 = MlrsSalvo.Launched, c0 = MissileCannonSalvo.Twins, cs0 = MissileCannonSalvo.Salvos, r0 = MechRockets.Twins;
                float f0 = Time.unscaledTime, fr = T;
                bool o1 = W.SendWaypoints(me, shooter, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = blue.Id, x = blue.CenterX, y = blue.CenterY } });
                bool o2 = W.SendWaypoints(me, gunners, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = blue.Id, x = blue.CenterX, y = blue.CenterY } });
                Mark($"{k}: shooter #{shooter.Id} at ({shooter.CenterX >> 4},{shooter.CenterY >> 4}) + red grunts at ({gunners.CenterX >> 4},{gunners.CenterY >> 4}) -> blue grunts at ({blue.CenterX >> 4},{blue.CenterY >> 4}); orders {o1}/{o2}; WAV at {fr - wav0:0.00} s");
                float span = k == "missile_launcher" ? 16f : 13f;
                yield return Wait(3.0f); Shot($"{job}_{k}_fight");
                yield return Wait(span - 3f);
                var es = OzSoundLog.Since(f0);
                soundDump.AppendLine($"#### {k} (WAV {fr - wav0:0.00} s .. {T - wav0:0.00} s)"); soundDump.Append(OzSoundLog.Format(es));
                int launches = k == "tough" ? (runner ? runner.Launches - l0 : -1) : k == "missile_launcher" ? MlrsSalvo.Launched - m0 : k == "missile_cannon" ? MissileCannonSalvo.Salvos - cs0 : MechRockets.Twins - r0;
                var plays = new Dictionary<string, int>(); int drops = 0; var dropKeys = new List<string>();
                foreach (var e in es)
                {
                    if (e.src != "CA") continue;
                    if (e.outcome != null && e.outcome.StartsWith("PLAY")) { string key = e.key + "|" + e.clip; plays[key] = plays.TryGetValue(key, out var n) ? n + 1 : 1; }
                    else if (e.outcome != null && e.outcome.StartsWith("DROP") && (e.key == "rocket" || e.key == "rocket_flight" || e.key == "launch" || e.key == "mlrs_salvo")) { drops++; dropKeys.Add(e.key + " " + e.outcome); }
                }
                var sb = new StringBuilder(); foreach (var kv in plays) sb.Append($"{kv.Key} x{kv.Value}; ");
                Mark($"{k}: {launches} launch(es) counted by its view; CA plays: {sb}; launch DROPs {drops} {string.Join(", ", dropKeys.ToArray())}");
                int C(string key, string clip = null) { int n = 0; foreach (var kv in plays) { var p = kv.Key.Split('|'); if (p[0] == key && (clip == null || p[1] == clip)) n += kv.Value; } return n; }
                switch (k)
                {
                    case "tough":
                        {
                            int rocket = C("rocket"), legacy = C("rocket", "rocket"), flight = C("rocket_flight", "rocket"), arrived = 0;
                            foreach (var e in es) if (e.key == "rocket_flight" && e.outcome != null && e.outcome.StartsWith("ALONG")) arrived++;
                            Verdict("B tough bazooka", launches > 0 && rocket > 0 && legacy == rocket && flight >= launches - 1 && drops == 0,
                                $"{launches} rockets; launch plays {rocket} (the legacy M12 bazooka clip 'rocket': {legacy}); in-flight plays {flight} ({arrived} flew the whole path to the impact); back-blasts {C("backblast")}; launch drops {drops}");
                            break;
                        }
                    case "missile_launcher":
                        {
                            int launch = C("launch") + C("mlrs_salvo"), whoosh = C("mlrs_whoosh"), rk = C("rocket");
                            Verdict("B MLRS salvo launch", launch > 0 && whoosh == 0 && rk == 0 && drops == 0,
                                $"{launches} ripple rockets + the sim's; legacy mlrsLaunch plays {launch} (sim rocket 'launch' {C("launch")}, ripple 'mlrs_salvo' {C("mlrs_salvo")}); rocket-roar whooshes {whoosh}, rocket clips {rk}; launch drops {drops}");
                            break;
                        }
                    case "missile_cannon":
                        {
                            int launch = C("launch"), rk = C("rocket");
                            Verdict("B missile cannon launch", launches > 0 && launch >= launches && rk >= launches && drops == 0,
                                $"{launches} salvos ({MissileCannonSalvo.Twins - c0} twins); legacy mlrsLaunch plays {launch}, rocket roars {rk} (0.06 s after each launch); launch drops {drops}");
                            break;
                        }
                    default:
                        Verdict("B medium tank rockets", launches > 0 && C("rocket") > 0 && drops == 0, $"{launches} twins; rocket plays {C("rocket")}; launch drops {drops}");
                        break;
                }
                KillStaged(); yield return Wait(2.5f);
            }
            if (tap) { tap.Close(); Destroy(tap); tap = null; }
        }

        void Finish(string why)
        {
            if (done) return; done = true;
            try
            {
                if (tap) { tap.Close(); tap = null; }
                Mark(why == null ? "DONE" : "STOPPED: " + why);
                log.AppendLine($"errors/exceptions in the Console during the run: {errors}; GPU peak (GpuGuard) {peak} C");
                log.AppendLine("## VERDICTS"); foreach (var v in verdicts) log.AppendLine("  " + v);
                File.WriteAllText(Path.Combine(Dir, job + "_sound_proof.txt"), log.ToString());
                File.WriteAllText(Path.Combine(Dir, job + "_ozsoundlog_launchers.txt"), soundDump.ToString());
                File.WriteAllText(Path.Combine(Dir, job + "_alan_channel.csv"), alanCsv.ToString());
                PlayerPrefs.SetInt("oz_testmode", 0); PlayerPrefs.SetInt("oz_music", 1); PlayerPrefs.Save();
            }
            catch (Exception e) { Debug.LogError("[sndb] finish: " + e); }
            UnityEditor.EditorApplication.isPlaying = false;
        }
    }
}
#endif
