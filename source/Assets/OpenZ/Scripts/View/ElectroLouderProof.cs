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
    /// One short fight at the L02 map middle: a red Electrobot, a red Tough squad and a red light tank attack an unkillable blue grunt
    /// squad. Phase A plays the Electrobot's zap / zap hit / recharge at the OLD table levels (the in-memory volDb lowered by the change),
    /// phase B at the NEW levels read from openz_sound_events.json. Per phase: the whole OzSoundLog, an Alan select + order line fed like
    /// PlayerInput, every Alan main-channel start, and the listener's float peak (samples over 0 dBFS); one WAV of the whole mix.
    /// </summary>
    [DefaultExecutionOrder(20)]   // after PlayerInput (0), before AudioDirector.PollInput (40), like SoundBundleProof
    public class ElectroLouderProof : MonoBehaviour
    {
        static readonly string Dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones/task_electro_louder"));
        static readonly string[] Ids = { "wpn_electro_fire", "imp_electro_hit", "wpn_electro_recharge" };
        static readonly float[] Raise = { 3f, 4f, 2f };                       // the change: zap +3 dB, zap hit +4 dB, recharge +2 dB
        static readonly float[] Want = { 8.7f, 3.3f, -0.9f };                 // new table values
        string job; float t0, lastMark, wav0; bool done; int errors; float hot = -1f, lastSample = -1f;
        readonly StringBuilder log = new StringBuilder(), soundDump = new StringBuilder(), alanCsv = new StringBuilder("wav_t,clip,len,vol\n");
        readonly List<string> verdicts = new List<string>();
        MusicV3PlayProof.Tap tap; PeakTap peakTap;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Auto()
        {
            string job = PlayerPrefs.GetString("oz_elloud_auto", "");
            if (string.IsNullOrEmpty(job)) return;
            PlayerPrefs.DeleteKey("oz_elloud_auto"); PlayerPrefs.Save();
            var go = new GameObject("ElectroLouderProof"); DontDestroyOnLoad(go);
            go.AddComponent<ElectroLouderProof>().job = job;
        }

        /// <summary>The listener's float mix: max |sample| and samples over 0 dBFS since the last reset.</summary>
        public class PeakTap : MonoBehaviour
        {
            public volatile float max; public volatile int over;
            void OnAudioFilterRead(float[] data, int channels)
            {
                float m = max; int o = 0;
                for (int i = 0; i < data.Length; i++) { float a = data[i] < 0f ? -data[i] : data[i]; if (a > m) m = a; if (a > 1f) o++; }
                max = m; if (o > 0) over += o;
            }
            public void ResetPeak() { max = 0f; over = 0; }
        }

        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        float T => (float)clock.Elapsed.TotalSeconds - t0;
        void Mark(string s) { lastMark = T; log.AppendLine($"[{T,7:0.00}] ## {s}"); Debug.Log("[elloud] " + s); }
        void Verdict(string step, bool ok, string what) { string v = $"{step}: {(ok ? "PASS" : "FAIL")} - {what}"; verdicts.Add(v); Mark("VERDICT " + v); }
        IEnumerator Wait(float s) { float end = T + s; while (T < end && !done) yield return null; }

        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; if (!done) Finish("disabled"); }
        void OnLog(string c, string st, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception) { errors++; log.AppendLine($"[{T,7:0.00}] ERROR {c}"); }
        }

        // ---------------------------------------------------------------- input fed like PlayerInput (before AudioDirector.PollInput)
        Action pending;
        IEnumerator Do(Action a) { pending = a; while (pending != null && !done) yield return null; }
        static readonly MethodInfo setSel = typeof(PlayerInput).GetProperty("SelectTime").GetSetMethod(true);
        static readonly MethodInfo setMark = typeof(PlayerInput).GetProperty("MarkerTime").GetSetMethod(true);
        static readonly MethodInfo setMarkPos = typeof(PlayerInput).GetProperty("MarkerPos").GetSetMethod(true);
        static readonly MethodInfo setHover = typeof(PlayerInput).GetProperty("Hover").GetSetMethod(true);
        void Select(ZObject u) { input.Selection.Clear(); input.Selection.Add(u); setSel.Invoke(input, new object[] { Time.unscaledTime }); AudioDirector.DebugClickFrame = Time.frameCount; }
        bool Order(ZObject u, ZObject target)
        {
            setHover.Invoke(input, new object[] { target });
            var wp = input.OrderFor(u, target, target.CenterX, target.CenterY);
            bool took = W.SendWaypoints(g.player, u, new List<ZWaypoint> { wp });
            setMarkPos.Invoke(input, new object[] { ZGame.PxToWorld(target.CenterX, target.CenterY) });
            setMark.Invoke(input, new object[] { Time.unscaledTime });
            return took;
        }

        // ---------------------------------------------------------------- the Alan main channel, every frame
        AudioSource alan; AudioClip aClip; bool aPlaying; int alanStarts;
        void Update()
        {
            if (pending != null) { var p = pending; pending = null; try { p(); } catch (Exception e) { Mark("action failed: " + e.Message); } }
            if (!alan) { var go = GameObject.Find("Voice_AlanMain"); if (go) alan = go.GetComponent<AudioSource>(); }
            if (!alan) return;
            bool pl = alan.isPlaying; var c = alan.clip;
            if (pl && c && (!aPlaying || c != aClip))
            {
                alanStarts++;
                alanCsv.AppendLine($"{T - wav0:0.00},{c.name},{c.length:0.00},{alan.volume:0.000}");
                log.AppendLine($"[{T,7:0.00}] alan start {c.name} ({c.length:0.00} s) vol {alan.volume:0.000}, WAV {T - wav0:0.00} s");
            }
            aPlaying = pl; aClip = c;
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
                if (T - lastMark > 120f) { Finish("watchdog: no progress for 120 s"); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        // ---------------------------------------------------------------- sim helpers (real units, real orders)
        ZGame g; ZWorld W; PlayerInput input; RtsCamera cam; Team me, them; AudioDirector ad;
        readonly List<ZObject> staged = new List<ZObject>();
        ZObject Fort(Team t) { foreach (var o in W.Objects) if (o.IsFort && o.Owner == t && !o.Destroyed) return o; return null; }
        static void Tough(ZObject o) { o.MaxHealth = 1000000000; o.SetHealth(o.MaxHealth); }
        ZObject Squad(string type, Team t, int px, int py)
        {
            OzCheatSpawner.FreeTile(W, px >> 4, py >> 4, 1, true, out int tx, out int ty);
            var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t); if (o == null) return null;
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            staged.Add(o); staged.AddRange(o.Minions); Tough(o); foreach (var m in o.Minions) Tough(m);
            return o;
        }
        ZObject Machine(string cat, string type, Team t, int px, int py)
        {
            OzCheatSpawner.FreeTile(W, px >> 4, py >> 4, 2, false, out int tx, out int ty);
            var o = W.CreateObject(cat, type, tx * 16, ty * 16, t); if (o == null) return null;
            g.SpawnUnitView(o); staged.Add(o); Tough(o); return o;
        }
        bool Attack(ZObject who, ZObject target) =>
            W.SendWaypoints(me, who, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = target.Id, x = target.CenterX, y = target.CenterY } });
        void Focus(int px, int py) { if (!cam) return; var p = ZGame.PxToWorld(px, py); cam.focus = new Vector3(p.x, 0f, p.z); cam.SetHeightImmediate(cam.height); }
        void Shot(string name)
        {
            try { ScreenCapture.CaptureScreenshot(Path.Combine(Dir, name + ".png")); Mark($"frame {name}.png (test-mode context frame, {Screen.width}x{Screen.height}, camera height {(cam ? cam.height : -1f):0} m)"); }
            catch (Exception e) { Mark("shot failed " + e.Message); }
        }
        void SetLevels(bool old)
        {
            for (int i = 0; i < Ids.Length; i++) { var d = ad.Def(Ids[i]); if (d != null) d.volDb = Want[i] - (old ? Raise[i] : 0f); }
        }

        // ---------------------------------------------------------------- the run
        IEnumerator Start()
        {
            t0 = 0f; lastMark = 0f; Directory.CreateDirectory(Dir);
            Mark($"electro louder proof {job} {DateTime.Now:yyyy-MM-dd HH:mm:ss}, scene {SceneManager.GetActiveScene().name}, oz_testmode {PlayerPrefs.GetInt("oz_testmode", 0)}, oz_music {PlayerPrefs.GetInt("oz_music", 1)}, screen {Screen.width}x{Screen.height}");
            StartCoroutine(Heat());
            float w0 = T;
            while ((!ZGame.I || ZGame.I.World == null || !AudioDirector.I || !FindFirstObjectByType<PlayerInput>()) && T - w0 < 90f) { lastMark = T; yield return null; }
            if (!ZGame.I || ZGame.I.World == null || !AudioDirector.I) { Finish("the level never came up within 90 s"); yield break; }
            Mark($"level up after {T - w0:0.0} s");
            g = ZGame.I; W = g.World; input = FindFirstObjectByType<PlayerInput>(); cam = FindFirstObjectByType<RtsCamera>(); ad = AudioDirector.I;
            me = g.player; them = me == Team.Red ? Team.Blue : Team.Red;
            int bots = W.Bots.Count; W.Bots.Clear();
            yield return Wait(4f);

            // 1. the new table values are live
            var tb = new StringBuilder(); bool live = true;
            for (int i = 0; i < Ids.Length; i++)
            {
                var d = ad.Def(Ids[i]);
                float v = d != null ? d.volDb : float.NaN; live &= d != null && Mathf.Abs(v - Want[i]) < 0.01f;
                tb.Append($"{Ids[i]} volDb {v:0.0} (was {Want[i] - Raise[i]:0.0}, +{Raise[i]:0} dB, bus {(d != null ? d.bus : "?")}, 3D {(d != null && d.is3D)}); ");
            }
            Verdict("T1 table live", live, tb.ToString());

            // 2. the fight
            var ourFort = Fort(me); var theirFort = Fort(them);
            int mx = (ourFort.CenterX + theirFort.CenterX) / 2, my = (ourFort.CenterY + theirFort.CenterY) / 2;
            int sx = mx - 40, tx = mx + 40, sy = my;
            var blue = Squad("grunt", them, tx, sy);
            var eb = Squad("electrobot", me, sx, sy);
            var tough = Squad("tough", me, sx, sy + 32);
            var tank = Machine("vehicle", "light", me, sx, sy - 32);
            if (blue == null || eb == null || tough == null || tank == null) { Finish($"staging failed: blue {blue != null}, electrobot {eb != null}, tough {tough != null}, light tank {tank != null}"); yield break; }
            Focus((sx + tx) / 2, sy);
            Mark($"CPU bots cleared ({bots}); red Electrobot #{eb.Id} at ({eb.CenterX >> 4},{eb.CenterY >> 4}), Tough squad #{tough.Id} at ({tough.CenterX >> 4},{tough.CenterY >> 4}), light tank #{tank.Id} at ({tank.CenterX >> 4},{tank.CenterY >> 4}) -> blue grunts #{blue.Id} at ({blue.CenterX >> 4},{blue.CenterY >> 4}); all unkillable; camera height {cam.height:0} m");
            yield return Wait(1.5f);

            var lis = FindFirstObjectByType<AudioListener>();
            foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) if (l.enabled) lis = l;
            wav0 = T;
            if (lis) { tap = lis.gameObject.AddComponent<MusicV3PlayProof.Tap>(); tap.Open(Path.Combine(Dir, job + "_fight_mix.wav")); peakTap = lis.gameObject.AddComponent<PeakTap>(); }
            Mark($"mix WAV from here: {job}_fight_mix.wav, listener '{(lis ? lis.name : "none")}', output {AudioSettings.outputSampleRate} Hz");

            yield return Phase("A_old", true, eb, tough, tank, blue);
            if (done) yield break;
            yield return Phase("B_new", false, eb, tough, tank, blue);
            if (tap) { tap.Close(); Destroy(tap); tap = null; }
            Finish(null);
        }

        IEnumerator Phase(string label, bool old, ZObject eb, ZObject tough, ZObject tank, ZObject blue)
        {
            SetLevels(old);
            var sb = new StringBuilder(); foreach (var id in Ids) sb.Append($"{id} {ad.Def(id).volDb:0.0} dB; ");
            float f0 = Time.unscaledTime, a0 = T - wav0; if (peakTap) peakTap.ResetPeak();
            Mark($"phase {label}: {sb}WAV from {a0:0.00} s");
            yield return Do(() => Select(eb));
            yield return Wait(0.4f);
            bool o1 = false; yield return Do(() => o1 = Order(eb, blue));
            bool o2 = Attack(tough, blue), o3 = Attack(tank, blue);
            Mark($"{label}: orders Electrobot {o1} (fed like PlayerInput: select + attack order voice), Tough {o2}, light tank {o3}");
            yield return Wait(3f); Shot($"{job}_{label}_fight");
            yield return Wait(7f);
            yield return Do(() => Select(eb));                                                        // a select line mid-fight
            yield return Wait(12f);
            var es = OzSoundLog.Since(f0);
            soundDump.AppendLine($"#### {label} (WAV {a0:0.00} s .. {T - wav0:0.00} s)"); soundDump.Append(OzSoundLog.Format(es));
            var plays = new SortedDictionary<string, int>(); int drops = 0;
            foreach (var e in es)
            {
                if (e.outcome != null && e.outcome.StartsWith("PLAY")) plays[e.src + " " + e.key] = plays.TryGetValue(e.src + " " + e.key, out var n) ? n + 1 : 1;
                else if (e.outcome != null && e.outcome.StartsWith("DROP") && e.key != null && e.key.Contains("electro")) drops++;
            }
            var pb = new StringBuilder(); foreach (var kv in plays) pb.Append($"{kv.Key} x{kv.Value}; ");
            float pk = peakTap ? peakTap.max : -1f; int over = peakTap ? peakTap.over : -1;
            Mark($"{label}: plays {pb}");
            Mark($"{label}: listener float peak {pk:0.000} ({20f * Mathf.Log10(Mathf.Max(1e-6f, pk)):0.0} dBFS), samples over 0 dBFS {over}, electro drops {drops}");
            int P(string k) { int n = 0; foreach (var kv in plays) if (kv.Key.EndsWith(" " + k)) n += kv.Value; return n; }
            Verdict($"{label} electro sounds played", P("wpn_electro_fire") > 0 && P("imp_electro_hit") > 0 && P("wpn_electro_recharge") > 0,
                $"zap {P("wpn_electro_fire")}, hit {P("imp_electro_hit")}, recharge {P("wpn_electro_recharge")}, charge {P("wpn_electro_charge")}");
            Verdict($"{label} no clipping", pk >= 0f && pk < 1f && over == 0, $"float peak {pk:0.000}, samples over 0 dBFS {over}");
        }

        void Finish(string why)
        {
            if (done) return; done = true;
            try
            {
                if (tap) { tap.Close(); tap = null; }
                Mark(why == null ? "DONE" : "STOPPED: " + why);
                log.AppendLine($"errors/exceptions in the Console during the run: {errors}; GPU peak (GpuGuard) {peak} C; Alan main-channel starts {alanStarts}");
                log.AppendLine("## VERDICTS"); foreach (var v in verdicts) log.AppendLine("  " + v);
                File.WriteAllText(Path.Combine(Dir, job + "_proof_log.txt"), log.ToString());
                File.WriteAllText(Path.Combine(Dir, job + "_ozsoundlog.txt"), soundDump.ToString());
                File.WriteAllText(Path.Combine(Dir, job + "_alan_channel.csv"), alanCsv.ToString());
                PlayerPrefs.SetInt("oz_testmode", 0); PlayerPrefs.SetInt("oz_music", 1); PlayerPrefs.Save();
            }
            catch (Exception e) { Debug.LogError("[elloud] finish: " + e); }
            UnityEditor.EditorApplication.isPlaying = false;
        }
    }
}
#endif
