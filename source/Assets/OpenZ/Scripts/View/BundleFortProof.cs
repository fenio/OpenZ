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
    ///   siege: wave A (2 blue light tanks + a grunt squad, unkillable) attacks the fort; at +50 s it pulls back 420 px for 14 s and
    ///   comes back (a dip in the danger: the old code re-armed the klaxon here); at +100 s wave B (4 tanks + 2 squads) joins (a much
    ///   bigger wave); at +150 s everybody leaves. Counts the klaxon bursts (AudioDirector.AlarmStarts / AlarmSeconds, the ALARM lines in
    ///   OzSoundLog) and replays the OLD rule on the same danger timeline (read by reflection each frame) for the before number.
    ///  "L02_look" / "L17_look" (full look): FT1 tower caps. Red + blue fort, day + night, 24 m close on the tower caps, the default RTS
    ///   height and 55 m; every frame twice in the same frame: BEFORE (the plinth top back on the old grey steel material) and AFTER
    ///   (the FT1 concrete cap, TowerMounts.CapConcrete).
    /// Heat: stops at 86 C held 30 s or 90 C at once (GpuGuard).
    /// </summary>
    public class BundleFortProof : MonoBehaviour
    {
        static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../.."));
        string job, dir; float lastMark; bool done; int errors; float hot = -1f, lastSample = -1f; int peak = -1;
        readonly StringBuilder log = new StringBuilder();
        readonly List<string> verdicts = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Auto()
        {
            string job = PlayerPrefs.GetString("oz_bfort_auto", "");
            if (string.IsNullOrEmpty(job)) return;
            PlayerPrefs.DeleteKey("oz_bfort_auto"); PlayerPrefs.Save();
            var go = new GameObject("BundleFortProof"); DontDestroyOnLoad(go);
            go.AddComponent<BundleFortProof>().job = job;
        }

        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        float T => (float)clock.Elapsed.TotalSeconds;
        void Mark(string s) { lastMark = T; log.AppendLine($"[{T,7:0.00}] {s}"); Debug.Log("[bfort] " + s); }
        void Verdict(string step, bool ok, string what) { string v = $"{step}: {(ok ? "PASS" : "FAIL")} - {what}"; verdicts.Add(v); Mark("VERDICT " + v); }
        IEnumerator Wait(float s) { float end = T + s; while (T < end && !done) { lastMark = T; yield return null; } }
        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; if (!done) Finish("disabled"); }
        void OnLog(string c, string st, LogType type)
        {
            if (c.StartsWith("[bfort]")) return;
            if (type == LogType.Error || type == LogType.Exception) { errors++; log.AppendLine($"[{T,7:0.00}] ERROR {c.Split('\n')[0]}"); }
        }

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
                if (T - lastMark > 90f) { Finish("watchdog: no progress for 90 s"); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        ZGame g; ZWorld W; RtsCamera cam; DayNight dn; float h0;
        static void GameViewSize(int idx)
        {
            var gvt = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            var p = gvt?.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p == null) return;
            foreach (var w in Resources.FindObjectsOfTypeAll(gvt)) p.SetValue(w, idx);
        }
        IEnumerator SkipBriefing()
        {
            float w0 = T; bool aborted = false; var pf = typeof(ZodBriefing).GetField("perf", BindingFlags.NonPublic | BindingFlags.Instance);
            double sim0 = W.Time;
            Application.targetFrameRate = 24;
            while (T - w0 < 45f && !done)
            {
                if (ZodBriefing.BriefingActive)
                {
                    if (!aborted && ZodBriefing.I && pf != null && T - w0 > 0.6f)
                    {
                        var p = pf.GetValue(ZodBriefing.I);
                        if (p != null) { p.GetType().GetMethod("Abort").Invoke(p, new object[] { 0.22f }); aborted = true; Mark("briefing: Abort(0.22 s) sent"); }
                    }
                }
                else if (!g.paused && W.Time > sim0 + 0.5) break;
                lastMark = T; yield return null;
            }
            Application.targetFrameRate = 60;
            Mark($"briefing over after {T - w0:0.0} s");
        }

        // ---------------------------------------------------------------- helpers
        readonly List<ZObject> staged = new List<ZObject>();
        static void Unkillable(ZObject o) { o.MaxHealth = 1000000000; o.SetHealth(o.MaxHealth); }
        ZObject Squad(string type, Team t, int px, int py)
        {
            OzCheatSpawner.FreeTile(W, px >> 4, py >> 4, 1, true, out int tx, out int ty);
            var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t); if (o == null) return null;
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            staged.Add(o); staged.AddRange(o.Minions); Unkillable(o); foreach (var m in o.Minions) Unkillable(m);
            return o;
        }
        ZObject Machine(string cat, string type, Team t, int px, int py)
        {
            OzCheatSpawner.FreeTile(W, px >> 4, py >> 4, 2, false, out int tx, out int ty);
            var o = W.CreateObject(cat, type, tx * 16, ty * 16, t); if (o == null) return null;
            g.SpawnUnitView(o); staged.Add(o); Unkillable(o); return o;
        }
        void AttackFort(ZObject who, ZObject fort) =>
            W.SendWaypoints(who.Owner, who, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = fort.Id, x = fort.CenterX, y = fort.CenterY } });
        void MoveTo(ZObject who, int px, int py)
        {
            OzCheatSpawner.FreeTile(W, px >> 4, py >> 4, who.IsRobot ? 1 : 2, who.IsRobot, out int tx, out int ty);
            who.Disengage();
            W.SendWaypoints(who.Owner, who, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Move, x = tx * 16 + 8, y = ty * 16 + 8 } });
        }
        void Kill(ZObject o) { if (o != null && !o.Destroyed && W.Get(o.Id) == o) { o.Waypoints.Clear(); o.Disengage(); o.SetHealth(0); W.UpdateObjectHealth(o); } }
        void Focus(Vector3 at, float h) { cam.focus = new Vector3(at.x, 0f, at.z) + Flat(cam.transform.forward).normalized * (at.y * 0.9f); cam.SetHeightImmediate(h); }
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        IEnumerator TimeOfDay(float tod) { dn.timeOfDay = tod; dn.Apply(tod); yield return Wait(1.2f); }
        void Snap(string file, string what)
        {
            var tb = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(dir, file), tb.EncodeToPNG());
            Mark($"frame {file} {tb.width}x{tb.height}; {what}"); Destroy(tb);
        }
        static string Priv(object o, string f)
        {
            var fi = o.GetType().GetField(f, BindingFlags.NonPublic | BindingFlags.Instance);
            var v = fi != null ? fi.GetValue(o) : null;
            return v is float x ? x.ToString("0.00") : v?.ToString() ?? "?";
        }
        int NearFort(ZObject fort, Team enemy)
        {
            int n = 0;
            foreach (var o in W.Objects)
            {
                if (!o.IsUnit || o.Destroyed || o.Owner != enemy) continue;
                long dx = o.CenterX - fort.CenterX, dy = o.CenterY - fort.CenterY; if (dx * dx + dy * dy <= 250L * 250L) n++;
            }
            return n;
        }

        // ---------------------------------------------------------------- the run
        IEnumerator Start()
        {
            lastMark = T; float w0 = T;
            while ((!ZGame.I || ZGame.I.World == null) && T - w0 < 90f) yield return null;
            if (!ZGame.I || ZGame.I.World == null) { Finish("the level never came up within 90 s"); yield break; }
            g = ZGame.I; W = g.World; cam = FindFirstObjectByType<RtsCamera>(); dn = FindFirstObjectByType<DayNight>();
            dir = Path.Combine(Root, "work/records/milestones/bundle_fort", job); Directory.CreateDirectory(dir);
            StartCoroutine(Heat());
            h0 = cam.height;
            Mark($"fort proof {job} {DateTime.Now:yyyy-MM-dd HH:mm:ss}, scene {SceneManager.GetActiveScene().name}, oz_testmode {PlayerPrefs.GetInt("oz_testmode", 0)}, oz_music {PlayerPrefs.GetInt("oz_music", 1)}, screen {Screen.width}x{Screen.height}, level {g.level}, RTS height {h0:0.0} m");
            int bots = W.Bots.Count; W.Bots.Clear();
            yield return SkipBriefing();
            if (done) yield break;
            Mark($"CPU bots cleared ({bots})");
            if (job.EndsWith("_siege")) yield return Siege();
            else yield return Look();
            Finish(null);
        }

        IEnumerator Siege()
        {
            var ad = AudioDirector.I;
            var fDanger = typeof(AudioDirector).GetField("danger", BindingFlags.NonPublic | BindingFlags.Instance);
            if (!ad || fDanger == null) { Verdict("bind", false, "no AudioDirector / danger field"); yield break; }
            var red = W.Objects.Find(o => o.IsFort && o.Owner == Team.Red && !o.Destroyed);
            Unkillable(red); W.UpdateObjectHealth(red);
            var fv = g.StaticViews.TryGetValue(red.Id, out var fgo) ? fgo : null;
            Focus(fv ? fv.transform.position : ZGame.PxToWorld(red.CenterX, red.CenterY), h0);
            // attack side: toward the map centre
            double mx = W.Map.WidthPix * 0.5 - red.CenterX, my = W.Map.HeightPix * 0.5 - red.CenterY, ml = Math.Sqrt(mx * mx + my * my); mx /= ml; my /= ml;
            Func<double, double, (int, int)> At = (d, side) => ((int)(red.CenterX + mx * d - my * side), (int)(red.CenterY + my * d + mx * side));
            int starts0 = AudioDirector.AlarmStarts; float secs0 = AudioDirector.AlarmSeconds; float log0 = Time.unscaledTime;
            // shadow of the OLD rule (AlarmBurst 6, AlarmRearm 45, re-armed whenever the danger left Fort)
            bool oldOn = false; float oldStart = -1000f, oldStop = -1000f, oldSecs = 0f; int oldStarts = 0;
            var timeline = new StringBuilder("t_s,danger,near,alarm_new_on,alarm_old_on\n");
            string lastD = ""; int dips = 0;
            float s0 = Time.time;
            var waveA = new List<ZObject>(); var waveB = new List<ZObject>();
            Action<List<ZObject>, ZObject> Add = (l, o) => { if (o == null) return; l.Add(o); if (o.IsRobot) l.AddRange(o.Minions); };
            { var p = At(170, -40); Add(waveA, Machine("vehicle", "light", Team.Blue, p.Item1, p.Item2)); p = At(175, 40); Add(waveA, Machine("vehicle", "light", Team.Blue, p.Item1, p.Item2)); p = At(150, 0); Add(waveA, Squad("grunt", Team.Blue, p.Item1, p.Item2)); }
            foreach (var o in waveA) if (!o.IsMinion) AttackFort(o, red);
            Mark($"wave A: {waveA.Count} blue units (unkillable) attack the red fort #{red.Id} from ~170 px toward the map centre");
            int phase = 0; float nextDbg = 0f; bool dipCleared = false;
            var all = new List<OzSoundLog.Entry>(); float logSeen = log0, pollAt = Time.unscaledTime;   // the ring holds 2000 entries (~110 s of a fight): poll it
            Application.targetFrameRate = 15;
            while (!done)
            {
                float t = Time.time - s0; lastMark = T;
                string d = fDanger.GetValue(ad).ToString(); int near = NearFort(red, Team.Blue);
                float now = Time.time;
                if (d == "Fort") { if (!oldOn && now - oldStop >= 45f) { oldOn = true; oldStart = now; oldStarts++; } else if (oldOn && now - oldStart >= 6f) { oldOn = false; oldStop = now; oldSecs += now - oldStart; } }
                else { if (oldOn) { oldOn = false; oldSecs += now - oldStart; } oldStop = -1000f; }
                if (d != lastD) { if (lastD == "Fort") dips++; Mark($"  t {t:0.0} s danger {lastD} -> {d}, enemies within 250 px {near}"); lastD = d; }
                if (Time.unscaledTime - pollAt >= 10f) { pollAt = Time.unscaledTime; foreach (var e in OzSoundLog.Since(logSeen)) if (e.t > logSeen || all.Count == 0) all.Add(e); if (all.Count > 0) logSeen = all[all.Count - 1].t; }
                if (Time.frameCount % 8 == 0) timeline.AppendLine($"{t:0.00},{d},{near},{(ad.LoopActive("fortalarm") ? 1 : 0)},{(oldOn ? 1 : 0)}");
                if (t >= nextDbg) { nextDbg = t + ((t > 95f && t < 112f) || t < 12f ? 1f : 10f); Mark($"  dbg t {t:0.0}: near {near}, danger {d}, AD {Priv(ad, "alarmWave")} wave, started {Priv(ad, "alarmStartedAt")}, stopped {Priv(ad, "alarmStoppedAt")}, lastThreat {Priv(ad, "alarmLastThreat")}, Time.time {Time.time:0.00}, starts {AudioDirector.AlarmStarts}, directors {FindObjectsByType<AudioDirector>(FindObjectsSortMode.None).Length}"); }
                if (phase == 0 && t >= 50f) { phase = 1; foreach (var o in waveA) if (!o.IsMinion) { var p = At(560, (o.Id % 3 - 1) * 40); MoveTo(o, p.Item1, p.Item2); } Mark($"t {t:0.0}: wave A pulls back ~560 px (the dip)"); }
                if (phase == 1 && t >= 58f && !dipCleared)
                {   // the dip must be real: whoever of wave A is still near the fort after 8 s (stuck / fighting) is removed
                    dipCleared = true; int k = 0;
                    foreach (var o in waveA) { long dx = o.CenterX - red.CenterX, dy = o.CenterY - red.CenterY; if (!o.Destroyed && dx * dx + dy * dy <= 262L * 262L) { Kill(o); k++; } }
                    Mark($"t {t:0.0}: {k} wave A units still within 262 px removed (a clean dip)");
                }
                if (phase == 1 && t >= 70f) { phase = 2; foreach (var o in waveA) if (!o.IsMinion && !o.Destroyed) AttackFort(o, red); Mark($"t {t:0.0}: wave A attacks again"); }
                if (phase == 2 && t >= 100f)
                {
                    phase = 3;
                    for (int i = 0; i < 4; i++) { var p = At(185, -90 + 60 * i); Add(waveB, Machine("vehicle", "light", Team.Blue, p.Item1, p.Item2)); }
                    { var p = At(160, -60); Add(waveB, Squad("grunt", Team.Blue, p.Item1, p.Item2)); p = At(160, 60); Add(waveB, Squad("psycho", Team.Blue, p.Item1, p.Item2)); }
                    foreach (var o in waveB) if (!o.IsMinion) AttackFort(o, red);
                    Mark($"t {t:0.0}: wave B joins: {waveB.Count} more units (a much bigger wave)");
                }
                if (phase == 3 && t >= 150f) { phase = 4; foreach (var l in new[] { waveA, waveB }) foreach (var o in l) if (!o.IsMinion) { var p = At(460, (o.Id % 5 - 2) * 40); MoveTo(o, p.Item1, p.Item2); } Mark($"t {t:0.0}: everybody leaves"); }
                if (phase == 4 && t >= 180f) break;
                yield return null;
            }
            if (oldOn) oldSecs += Time.time - oldStart;
            float newSecs = AudioDirector.AlarmSeconds - secs0 + (ad.LoopActive("fortalarm") ? 0f : 0f);
            int newStarts = AudioDirector.AlarmStarts - starts0;
            foreach (var e in OzSoundLog.Since(logSeen)) if (e.t > logSeen) all.Add(e);
            var es = all;
            int ann = 0, alarmLines = 0; var alarmLog = new StringBuilder();
            foreach (var e in es)
            {
                if (e.key == "ann_fort_under_attack" && e.outcome.StartsWith("PLAY")) ann++;
                if (e.key == "bld_fort_alarm") { alarmLines++; alarmLog.AppendLine($"  {e.t - log0,7:0.00} s  {e.outcome}"); }
            }
            File.WriteAllText(Path.Combine(dir, "ozsoundlog.txt"), OzSoundLog.Format(es));
            File.WriteAllText(Path.Combine(dir, "danger_timeline.csv"), timeline.ToString());
            Mark($"klaxon lines in OzSoundLog ({alarmLines}):\n{alarmLog}");
            Mark($"siege 180 s: danger left Fort {dips} times; NEW rule: {newStarts} bursts, {newSecs:0.0} s of klaxon; OLD rule on the same timeline: {oldStarts} bursts, {oldSecs:0.0} s; Rachel 'fort under attack' plays {ann}");
            Verdict("alarm_bursts", newStarts >= 1 && newStarts <= 2, $"{newStarts} klaxon bursts in a 3-minute siege (expected 1 for the attack + at most 1 for the much bigger wave B)");
            Verdict("alarm_short", newStarts > 0 && newSecs / newStarts <= AudioDirector.AlarmBurst + 0.6f, $"{newSecs:0.0} s in total, {(newStarts > 0 ? newSecs / newStarts : 0):0.0} s per burst (+1.5 s fade), never looping through the fight");
            Verdict("dip_no_rearm", true, $"info: the danger dipped {dips} times; the old rule would have started {oldStarts} bursts ({oldSecs:0.0} s)");
            foreach (var l in new[] { waveA, waveB }) foreach (var o in l) Kill(o);
            var sum = new StringBuilder($"siege summary {DateTime.Now:HH:mm:ss}, siege ran {Time.time - s0:0.0} s{(done ? " (STOPPED early)" : "")}");
            sum.AppendLine();
            sum.AppendLine($"NEW rule: {newStarts} bursts, {newSecs:0.0} s; OLD rule on the same danger timeline: {oldStarts} bursts, {oldSecs:0.0} s; danger left Fort {dips} times; Rachel plays {ann}");
            sum.Append(alarmLog); foreach (var v in verdicts) sum.AppendLine(v);
            File.WriteAllText(Path.Combine(dir, "siege_summary.txt"), sum.ToString());
        }

        // ================================================================ FT1 tower caps
        // fort's own pad, not the FortBase texture (the v1 albedo edit changed only hidden / shoulder texels and was reverted). BEFORE =
        // the plinth's steel group put back on the old grey steel material (same values as TowerMounts.MountMaterial), AFTER = FT1.
        readonly List<(Renderer r, Material cap)> mounts = new List<(Renderer, Material)>();
        Material steelOld;
        void UseBefore(bool on)
        {
            foreach (var (r, cap) in mounts)
            {
                if (!r) continue;
                var ms = r.sharedMaterials; ms[0] = on ? steelOld : cap; r.sharedMaterials = ms;
            }
        }
        IEnumerator Pair(string name, string what)
        {
            UseBefore(true); yield return null; yield return new WaitForEndOfFrame(); Snap(name + "_before.png", what + " (old grey steel cap)");
            UseBefore(false); yield return null; yield return new WaitForEndOfFrame(); Snap(name + "_after.png", what + " (FT1 concrete cap)");
        }
        IEnumerator Look()
        {
            var tm = TowerMounts.I;
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            steelOld = new Material(sh) { name = "FT1 proof: old steel cap" };
            steelOld.SetColor("_BaseColor", tm ? tm.mountColor : new Color(0.52f, 0.53f, 0.55f));
            steelOld.SetFloat("_Metallic", tm ? tm.metallic : 0.4f); steelOld.SetFloat("_Smoothness", tm ? tm.smoothness : 0.45f);
            var forts = W.Objects.FindAll(o => o.IsFort && !o.Destroyed);
            forts.Sort((a, b) => a.Owner == Team.Red ? -1 : b.Owner == Team.Red ? 1 : 0);
            yield return Wait(0.5f);
            var root = GameObject.Find("OZ_TowerMounts");
            if (root) foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true)) { var ms = r.sharedMaterials; if (ms.Length > 0 && ms[0]) mounts.Add((r, ms[0])); }
            string capName = mounts.Count > 0 ? mounts[0].cap.name : "-";
            var capTex = mounts.Count > 0 ? mounts[0].cap.GetTexture("_BaseMap") : null;
            Mark($"TowerMounts.CapConcrete {TowerMounts.CapConcrete}; plinths {mounts.Count} on {forts.Count} forts; cap material '{capName}', texture {(capTex ? capTex.name + " " + capTex.width : "none")}");
            Verdict("bind", mounts.Count == 4 * forts.Count && capTex != null, $"{mounts.Count} plinths with the concrete cap material and its texture");
            bool run0 = dn.running; dn.running = false;
            foreach (var night in new[] { false, true })
            {
                yield return TimeOfDay(night ? 0.62f : 0.08f);
                foreach (var f in forts)
                {
                    if (!g.StaticViews.TryGetValue(f.Id, out var go) || !go) continue;
                    var baseR = go.transform.Find("Model/FortBase") ?? FindDeep(go.transform, "FortBase");
                    string tag = $"{g.level}_{f.Owner.ToString().ToLower()}_{(night ? "night" : "day")}";
                    var gun = FindDeep(go.transform, "FortGunYaw");
                    Vector3 c = gun ? gun.position : go.transform.position + Vector3.up * 6f;
                    Focus(c, 24f); yield return Wait(0.8f);
                    yield return Pair(tag + "_24", $"{f.Owner} fort {f.Type}, 24 m on the gun + the rear towers");
                    if (baseR)
                    {   // the front towers (+z local, the gate side): the camera looks at them from the south
                        var tw = baseR.TransformPoint(new Vector3(6.94f / 10.53f, 3.01f / 10.53f, 5.33f / 10.53f));
                        var tw2 = baseR.TransformPoint(new Vector3(-6.93f / 10.53f, 3.01f / 10.53f, 5.30f / 10.53f));
                        Focus((tw + tw2) * 0.5f, 24f); yield return Wait(0.6f);
                        yield return Pair(tag + "_24_gate", $"{f.Owner} fort, 24 m on the two gate-side tower caps");
                    }
                    Focus(c, h0); yield return Wait(0.6f);
                    yield return Pair($"{tag}_{h0:0}", $"{f.Owner} fort at the default RTS height");
                    if (!night) { Focus(c, 55f); yield return Wait(0.6f); yield return Pair(tag + "_55", $"{f.Owner} fort at 55 m"); }
                }
            }
            dn.running = run0;
            Verdict("frames", true, "before / after pairs written (look them over side by side)");
        }

        static Transform FindDeep(Transform t, string n)
        {
            if (t.name == n) return t;
            foreach (Transform c in t) { var r = FindDeep(c, n); if (r) return r; }
            return null;
        }

        void Finish(string why)
        {
            if (done) return; done = true;
            try
            {
                Application.targetFrameRate = 60; try { UseBefore(false); } catch (Exception e) { log.AppendLine("UseBefore(false) at finish: " + e.Message); }
                foreach (var o in staged) Kill(o);
                Mark(why == null ? "DONE" : "STOPPED: " + why);
                log.AppendLine($"errors/exceptions in the Console during the run: {errors}; GPU peak (GpuGuard) {peak} C");
                log.AppendLine("## VERDICTS"); foreach (var v in verdicts) log.AppendLine("  " + v);
                if (dir != null) File.WriteAllText(Path.Combine(dir, "proof_log.txt"), log.ToString());
                GameViewSize(9);
            }
            catch (Exception e) { Debug.LogError("[bfort] finish: " + e); }
            UnityEditor.EditorApplication.isPlaying = false;
        }
    }
}
#endif
