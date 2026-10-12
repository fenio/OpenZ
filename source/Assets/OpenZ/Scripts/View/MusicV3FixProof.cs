#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// music_v3 fix-job Play proof (editor only): the README_IMPORT.md steps the first proof left out, with REAL units where the
    /// README asks for them. PlayerPrefs oz_mv3fix_auto before Play:
    ///  "MENU_L02" (open MainMenu, oz_testmode 0): step 0 (Zod's menu greeting: the subtle eased duck), then DEPLOY L02 through the
    ///     real menu flow in the GPU-light test mode: step 13 (the musicV3 switch), step 9 (a real skirmish away from the forts:
    ///     the mood switch), step 6 (a real attack on THEIR fort: spawned red units ordered onto its guns through PlayerInput's
    ///     right-click order, then pulled back), step 12 (music off / on during that drama and on the end screen), step 8 (the
    ///     pause menu during the drama with the fort already quiet: no "attack over" while paused), the fade back + resume check,
    ///     the win and the end music.
    ///  "L17" (open L17_CarPark, oz_testmode 1, oz_music 0): step 12's "a new level with oz_music 0" (no start v), music on, a
    ///     forced big attack cycle (the resume check under the 50 ms tolerance), then step 6 on L17 (a real attack + pull back,
    ///     a second resume check).
    ///  "L17W" (open L17_CarPark, oz_testmode 1, oz_music 1): work item H17 on L17 through the real OpenZ > Music v3 menu items:
    ///     Status on a calm song, Skip (README step 2), a real skirmish (battle song), a big attack on OUR fort (fort-danger drama),
    ///     then a WIN (their fort falls) -> the drama to the end screen, then a VICTORY song; no back-to-back repeats.
    /// Logic runs in the test mode; the proof frames switch to the full look for ~2 s each (TestModeRuntime off, atmosphere and
    /// the recorded camera AA / MSAA / shadows back), then back to the light mode. The CPU bots are cleared (a controlled proof).
    /// </summary>
    public class MusicV3FixProof : MonoBehaviour
    {
        static readonly string Dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones/music_v3/fix"));
        string job, phase = "boot"; float t0, lastMark; readonly StringBuilder log = new StringBuilder(), csv = new StringBuilder();
        readonly List<(float t, string s)> lines = new List<(float, string)>();
        readonly List<string> verdicts = new List<string>();
        int errors; bool done, fullLook; float hot = -1f, lastSample = -1f;
        MusicV3PlayProof.Tap tap;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Auto()
        {
            string job = PlayerPrefs.GetString("oz_mv3fix_auto", "");
            if (string.IsNullOrEmpty(job)) return;
            PlayerPrefs.DeleteKey("oz_mv3fix_auto"); PlayerPrefs.Save();
            var go = new GameObject("MusicV3FixProof"); DontDestroyOnLoad(go);
            var p = go.AddComponent<MusicV3FixProof>(); p.job = job; p.RecordCams();   // before TestModeRuntime.Start strips the cameras
        }

        float T => Time.unscaledTime - t0;
        void Mark(string s) { lastMark = T; log.AppendLine($"[{T,7:0.00}] ## {s}"); Debug.Log("[mv3fix] " + s); }
        void Verdict(string step, bool ok, string what) { string v = $"{step}: {(ok ? "PASS" : "FAIL")} - {what}"; verdicts.Add(v); Mark("VERDICT " + v); }
        void Snap(string s) { var ad = AudioDirector.I; log.AppendLine($"[{T,7:0.00}] ## SNAP {s}\n{(ad ? ad.MusicV3Status() : "no AudioDirector")}\n  {Lp()}"); }
        bool SeenSince(string needle, float since) { foreach (var l in lines) if (l.t >= since && l.s.Contains(needle)) return true; return false; }
        float FirstSince(string needle, float since) { foreach (var l in lines) if (l.t >= since && l.s.Contains(needle)) return l.t; return -1f; }
        string LineSince(string needle, float since) { foreach (var l in lines) if (l.t >= since && l.s.Contains(needle)) return l.s; return null; }
        IEnumerator WaitLine(string needle, float since, float max) { float w = 0f; while (!SeenSince(needle, since) && w < max && !done) { w += Time.unscaledDeltaTime; yield return null; } }
        IEnumerator Wait(float s) { float w = 0f; while (w < s && !done) { w += Time.unscaledDeltaTime; yield return null; } }

        void OnEnable() { Application.logMessageReceived += OnLog; SceneManager.sceneLoaded += OnScene; }
        void OnDisable() { Application.logMessageReceived -= OnLog; SceneManager.sceneLoaded -= OnScene; if (!done) Finish("disabled"); }
        void OnLog(string c, string st, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception) { errors++; lines.Add((T, "ERROR " + c)); log.AppendLine($"[{T,7:0.00}] ERROR {c}"); return; }
            // the OpenZ > Music v3 > Status dump ("[MusicV3] music_v3 <layer> (..." + the whole history): into the log only, never an event
            if (c.StartsWith("[MusicV3] music_v3 ")) { log.AppendLine($"[{T,7:0.00}] STATUS DUMP {c}"); return; }
            if (c.StartsWith("[MusicV3]") || c.StartsWith("[ZodCameo]") || c.StartsWith("[OpenZ Menu]") || c.StartsWith("[Victory") || c.StartsWith("[OpenZVictory") || c.StartsWith("OpenZ test mode"))
            { lines.Add((T, c)); log.AppendLine($"[{T,7:0.00}] {c}"); }
        }

        // ---------------------------------------------------------------- full look for the proof frames only
        sealed class CamRec { public Camera c; public bool msaa, shadows; public AntialiasingMode aa; public AntialiasingQuality q; }
        readonly List<CamRec> cams = new List<CamRec>();
        void OnScene(Scene s, LoadSceneMode m) { if (m == LoadSceneMode.Single) RecordCams(); }
        void RecordCams()
        {
            cams.Clear();
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                var d = c.GetComponent<UniversalAdditionalCameraData>();
                cams.Add(new CamRec { c = c, msaa = c.allowMSAA, shadows = d ? d.renderShadows : true, aa = d ? d.antialiasing : AntialiasingMode.None, q = d ? d.antialiasingQuality : AntialiasingQuality.High });
            }
        }
        IEnumerator FullLook()
        {
            if (!GameBoot.TestMode) { fullLook = true; yield break; }
            foreach (var tm in FindObjectsByType<TestModeRuntime>(FindObjectsSortMode.None)) Destroy(tm.gameObject);   // its OnDisable re-activates SSAO + Atmosphere
            foreach (var r in cams)
            {
                if (!r.c) continue; r.c.allowMSAA = r.msaa;
                var d = r.c.GetComponent<UniversalAdditionalCameraData>(); if (d) { d.renderShadows = r.shadows; d.antialiasing = r.aa; d.antialiasingQuality = r.q; }
            }
            foreach (var a in FindObjectsByType<Atmosphere>(FindObjectsInactive.Include, FindObjectsSortMode.None)) a.enabled = true;   // OnEnable rebuilds the sand / devils
            fullLook = true;
            yield return Wait(1.6f);                                                                    // TAA / SSAO / fog settle
        }
        void LightLook()
        {
            if (!GameBoot.TestMode) { fullLook = false; return; }
            foreach (var a in FindObjectsByType<Atmosphere>(FindObjectsSortMode.None))
            { a.enabled = false; foreach (var ps in a.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false); }
            Shader.SetGlobalFloat("_AtmoEnabled", 0f);
            new GameObject("OZ_TestMode") { hideFlags = HideFlags.DontSave }.AddComponent<TestModeRuntime>();
            fullLook = false;
        }
        readonly List<string> shots = new List<string>();
        IEnumerator Frame(string name, Vector3? focus = null)
        {
            var cam = FindFirstObjectByType<RtsCamera>();
            if (cam && focus.HasValue) { cam.focus = focus.Value; cam.SetHeightImmediate(cam.height); }
            yield return FullLook();
            string p = Path.Combine(Dir, name + ".png");
            try { ScreenCapture.CaptureScreenshot(p); shots.Add(name + ".png"); } catch (Exception e) { log.AppendLine("shot failed " + e.Message); }
            yield return null; yield return null;
            Mark($"frame {name}.png (full look, {Screen.width}x{Screen.height}, camera height {(cam ? cam.height : -1f):0} m)");
            LightLook();
        }

        // ---------------------------------------------------------------- probes
        static readonly MethodInfo miDuck = typeof(AudioDirector).GetMethod("MusicDuckDb", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo fShared = typeof(AudioDirector).GetField("duckMusic", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo fA = typeof(AudioDirector).GetField("musicA", BindingFlags.NonPublic | BindingFlags.Instance), fB = typeof(AudioDirector).GetField("musicB", BindingFlags.NonPublic | BindingFlags.Instance);
        static float BusDuck() { var ad = AudioDirector.I; try { return ad && miDuck != null ? (float)miDuck.Invoke(ad, null) : 0f; } catch { return 0f; } }
        static float SharedDuck() { var ad = AudioDirector.I; try { return ad && fShared != null ? (float)fShared.GetValue(ad) : 0f; } catch { return 0f; } }
        static AudioSource V2(FieldInfo f) { var ad = AudioDirector.I; if (!ad || f == null) return null; var v = f.GetValue(ad); return v?.GetType().GetField("src")?.GetValue(v) as AudioSource; }
        static string V2Desc() { string s = ""; foreach (var f in new[] { fA, fB }) { var a = V2(f); s += a ? $"{(a.clip ? a.clip.name : "-")} {(a.isPlaying ? "on" : "off")} vol {a.volume:0.000} t {a.time:0.0}; " : "-; "; } return s; }
        static string Lp()
        {
            var sb = new StringBuilder("music_v3 voice filters:");
            foreach (var lp in FindObjectsByType<AudioLowPassFilter>(FindObjectsSortMode.None))
                if (lp.gameObject.name.StartsWith("MusicV3_")) { var s = lp.GetComponent<AudioSource>(); sb.Append($" {lp.gameObject.name} lp {(lp.enabled ? "ON 900 Hz" : "off")} vol {(s ? s.volume : 0f):0.000} {(s && s.isPlaying ? "playing" : "not playing")};"); }
            return sb.ToString();
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
        void Record(string name)
        {
            if (tap) { tap.Close(); Destroy(tap); tap = null; }
            var lis = OzLoudnessMeter.I ? OzLoudnessMeter.I.gameObject : FindFirstObjectByType<AudioListener>()?.gameObject;
            if (lis) { tap = lis.AddComponent<MusicV3PlayProof.Tap>(); tap.Open(Path.Combine(Dir, name + "_mix.wav")); Mark("recording the listener output to " + name + "_mix.wav"); }
            else Mark("NO listener to record");
        }

        static readonly Regex rxLayer = new Regex(@"music_v3 (\w+) \("), rxDuck = new Regex(@"duck (-?[\d.]+) dB \(zod (-?[\d.]+)\)"), rxVoice = new Regex(@"  (v\d: \w+ \w+ [^\n]*)");
        IEnumerator Sample()
        {
            csv.AppendLine("t,phase,sim_t,paused,layer,bus_duck_db,shared_duck_db,zod_speaking,momentary_lufs,short_lufs,gpu_c,look,v2_voices,v3_voices");
            while (!done)
            {
                var ad = AudioDirector.I; int gpu = GpuTemp(); var g = ZGame.I;
                string st = ad ? ad.MusicV3Status() : "";
                var ml = rxLayer.Match(st);
                var vs = new List<string>(); foreach (Match m in rxVoice.Matches(st)) vs.Add(m.Groups[1].Value.Trim());
                var lm = OzLoudnessMeter.I;
                csv.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:0.00},{1},{2:0.00},{3},{4},{5:0.00},{6:0.00},{7},{8:0.0},{9:0.0},{10},{11},\"{12}\",\"{13}\"", T, phase,
                    g && g.World != null ? g.World.Time : -1, g && g.paused ? 1 : 0, ml.Success ? ml.Groups[1].Value : "-", BusDuck(), SharedDuck(), ZodBriefing.Speaking ? 1 : 0,
                    lm ? lm.Momentary : -120f, lm ? lm.ShortTerm : -120f, gpu, fullLook ? "full" : GameBoot.TestMode ? "light" : "full", V2Desc(), string.Join(" | ", vs)));
                if (gpu >= 90) { Finish("GPU 90 C"); yield break; }
                float step = lastSample < 0f ? 0.1f : T - lastSample; lastSample = T;
                if (gpu >= 86) { hot = hot < 0f ? step : hot + step; if (hot >= 30f) { Finish("GPU >= 86 C for 30 s (hysteresis 84 C)"); yield break; } }
                else if (gpu >= 0 && gpu < 84) hot = -1f;
                if (T - lastMark > 240f) { Finish("watchdog: no progress for 240 s"); yield break; }
                yield return new WaitForSecondsRealtime(phase == "menu" ? 0.1f : 0.25f);
            }
        }

        // ---------------------------------------------------------------- sim helpers (real units, real orders)
        ZGame g; ZWorld W; PlayerInput input; readonly List<ZObject> staged = new List<ZObject>();
        static double D(ZObject a, int x, int y) { double dx = a.CenterX - x, dy = a.CenterY - y; return Math.Sqrt(dx * dx + dy * dy); }
        ZObject Fort(Team t) { foreach (var o in W.Objects) if (o.IsFort && o.Owner == t && !o.Destroyed) return o; return null; }
        ZObject Squad(string type, Team t, int px, int py)
        {
            OzCheatSpawner.FreeTile(W, px >> 4, py >> 4, 1, true, out int tx, out int ty);
            var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t); g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            staged.Add(o); staged.AddRange(o.Minions); return o;
        }
        ZObject Vehicle(string type, Team t, int px, int py)
        {
            OzCheatSpawner.FreeTile(W, px >> 4, py >> 4, 2, false, out int tx, out int ty);
            var o = W.CreateObject("vehicle", type, tx * 16, ty * 16, t); g.SpawnUnitView(o); staged.Add(o); return o;
        }
        bool Order(ZObject u, ZObject target, int x, int y) { var wp = input.OrderFor(u, target, x, y); return W.SendWaypoints(g.player, u, new List<ZWaypoint> { wp }); }
        void Kill(IEnumerable<ZObject> list) { foreach (var m in new List<ZObject>(list)) if (m != null && !m.Destroyed && W.Get(m.Id) == m) { m.SetHealth(0); W.UpdateObjectHealth(m); } }
        static int Alive(IEnumerable<ZObject> list) { int n = 0; foreach (var o in list) if (o != null && !o.Destroyed) n++; return n; }
        FortReading Theirs() { var ad = AudioDirector.I; return MusicFortReader.Read(W, g.player == Team.Red ? Team.Blue : Team.Red, ad.musicV3Config); }
        Vector3 Mid(IEnumerable<ZObject> list, ZObject also)
        {
            double x = 0, y = 0; int n = 0; foreach (var o in list) if (o != null && !o.Destroyed) { x += o.CenterX; y += o.CenterY; n++; }
            if (n == 0) return ZGame.PxToWorld(also.CenterX, also.CenterY);
            return ZGame.PxToWorld((x / n + also.CenterX) * 0.5, (y / n + also.CenterY) * 0.5);
        }

        // ---------------------------------------------------------------- the runs
        IEnumerator Start()
        {
            t0 = Time.unscaledTime; lastMark = 0f; Directory.CreateDirectory(Dir);
            Mark($"music_v3 fix proof {job} {DateTime.Now:yyyy-MM-dd HH:mm:ss}, scene {SceneManager.GetActiveScene().name}, oz_testmode {PlayerPrefs.GetInt("oz_testmode", 0)}, oz_music {PlayerPrefs.GetInt("oz_music", 1)}, screen {Screen.width}x{Screen.height}");
            StartCoroutine(Sample());
            if (job == "MENU_L02") { yield return MenuStep(); if (done) yield break; yield return L02(); }
            else if (job == "L17") yield return L17();
            else if (job == "L02B") yield return L02B();
            else if (job == "L17W") yield return L17W();
            else Mark("unknown job " + job);
            Finish(null);
        }

        IEnumerator MenuStep()
        {
            phase = "menu";
            float w = 0f; OpenZMainMenu menu = null;
            while ((!(menu = FindFirstObjectByType<OpenZMainMenu>()) || !AudioDirector.I) && w < 60f) { w += Time.unscaledDeltaTime; yield return null; }
            if (!menu) { Verdict("step 0", false, "no main menu"); yield break; }
            Record("menu");
            Mark("main menu live; music_v2 voices: " + V2Desc() + " | " + (AudioDirector.I ? AudioDirector.I.MusicV3Status() : ""));
            w = 0f; while (!ZodBriefing.Speaking && w < 60f && !done) { w += Time.unscaledDeltaTime; yield return null; }
            if (!ZodBriefing.Speaking) { Verdict("step 0", false, "Zod's menu greeting never started within 60 s"); }
            else
            {
                float s0 = T; string songBefore = V2Desc();
                Mark("Zod's menu greeting started; music_v2 voices " + songBefore);
                yield return Wait(2.5f); Snap("menu greeting"); Mark($"bus duck {BusDuck():0.00} dB, shared duck {SharedDuck():0.00} dB, voices {V2Desc()}");
                StartCoroutine(MenuShot());
                w = 0f; while (ZodBriefing.Speaking && w < 40f && !done) { w += Time.unscaledDeltaTime; yield return null; }
                float s1 = T; Mark("Zod's menu greeting over after " + (s1 - s0).ToString("0.0") + " s");
                yield return Wait(3.5f);
                string songAfter = V2Desc(); Mark("3.5 s after his last word: bus duck " + BusDuck().ToString("0.00") + " dB, voices " + songAfter);
                menuWindow = (s0, s1);
            }
            // the level-select screen, then DEPLOY L02 through the real menu flow, in the GPU-light test mode
            PlayerPrefs.SetInt("oz_testmode", 1); PlayerPrefs.Save();
            var mt = typeof(OpenZMainMenu); const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
            mt.GetMethod("GoSelect", F)?.Invoke(menu, null); yield return Wait(0.5f);
            mt.GetMethod("FocusCard", F)?.Invoke(menu, new object[] { 0, true }); yield return Wait(2.5f);
            Mark("level select, L02 focused; DEPLOY (oz_testmode 1 for the logic run)");
            float d0 = T; mt.GetMethod("Deploy", F)?.Invoke(menu, null);
            phase = "loading";
            w = 0f; while ((!AudioDirector.I || AudioDirector.I.MusicV3Core == null || !ZGame.I || ZGame.I.World == null) && w < 240f && !done) { w += Time.unscaledDeltaTime; yield return null; }
            Mark($"L02 live {T - d0:0.0} s after DEPLOY");
        }
        (float a, float b) menuWindow = (-1f, -1f);
        IEnumerator MenuShot() { yield return Wait(1.0f); yield return Frame("menu_greeting"); }

        void LevelSetup(string lv)
        {
            g = ZGame.I; W = g.World; input = FindFirstObjectByType<PlayerInput>();
            int bots = W.Bots.Count; W.Bots.Clear();
            AudioDirector.MusicV3Overlay = true;
            if (GameBoot.TestMode) { QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60; }
            Mark($"{lv} setup: {bots} CPU bot(s) cleared (controlled proof), player {g.player}, input {(input ? "PlayerInput" : "NONE")}, test mode {GameBoot.TestMode}, 60 fps cap");
        }

        IEnumerator L02()
        {
            phase = "L02";
            float w = 0f; while (FindFirstObjectByType<TestModeRuntime>() == null && GameBoot.TestMode && w < 5f) { w += Time.unscaledDeltaTime; yield return null; }
            LevelSetup("L02"); Record("L02"); yield return Wait(4f); Snap("L02 start");
            var ad = AudioDirector.I; Team me = g.player, them = me == Team.Red ? Team.Blue : Team.Red;
            var ourFort = Fort(me); var theirFort = Fort(them);
            if (ourFort == null || theirFort == null) { Verdict("setup", false, "forts not found"); yield break; }

            // ---- step 13: the musicV3 switch
            float a13 = T; ad.musicV3 = false; Mark("step 13: musicV3 unticked");
            yield return WaitLine("retired: the music_v3 voices are stopped", a13, 8f);
            yield return Wait(5f); string v2 = V2Desc(); Snap("musicV3 off (music_v2 plays)"); Mark("music_v2 voices: " + v2);
            float b13 = T; ad.musicV3 = true; Mark("step 13: musicV3 ticked again");
            yield return WaitLine("[MusicV3] live:", b13, 8f); yield return Wait(4f); Snap("musicV3 on again");
            bool v2Played = v2.Contains(" on vol");
            Verdict("step 13 (musicV3 switch)", SeenSince("retired: the music_v3 voices are stopped", a13) && v2Played && SeenSince("the music_v2 voices were playing: faded out 1 s", b13) && SeenSince("[MusicV3] live:", b13),
                $"off: {(SeenSince("retired: the music_v3 voices are stopped", a13) ? "retired" : "NO retire line")}, music_v2 {(v2Played ? "played" : "did NOT play")} ({v2.Trim()}); on: {(SeenSince("the music_v2 voices were playing: faded out 1 s", b13) ? "music_v2 faded out 1 s" : "NO music_v2 fade line")}, {(SeenSince("[MusicV3] live:", b13) ? "live again" : "NOT live")}");

            // ---- step 9: a real skirmish away from the forts -> mood Calm -> Battle, then Battle -> Calm
            int mx = (ourFort.CenterX + theirFort.CenterX) / 2, my = (ourFort.CenterY + theirFort.CenterY) / 2;
            float s9 = T; var red = Squad("grunt", me, mx - 40, my); var blue = Squad("grunt", them, mx + 40, my);
            var sk = new List<ZObject> { red, blue }; sk.AddRange(red.Minions); sk.AddRange(blue.Minions);
            foreach (var o in sk) { o.MaxHealth = 1000000000; o.SetHealth(o.MaxHealth); }                     // the fight lasts until they are removed
            bool sent = Order(red, blue, blue.CenterX, blue.CenterY);
            Mark($"step 9: red grunt squad ({1 + red.Minions.Count}) + blue grunt squad ({1 + blue.Minions.Count}) at the map middle ({mx >> 4},{my >> 4}) tiles, {D(red, ourFort.CenterX, ourFort.CenterY) / 16:0} / {D(blue, theirFort.CenterX, theirFort.CenterY) / 16:0} tiles from the forts; red ordered onto blue: {sent}");
            w = 0f; while (!SeenSince("mood Calm -> Battle", s9) && w < 60f && !done) { w += Time.unscaledDeltaTime; yield return null; }
            float mb = FirstSince("mood Calm -> Battle", s9);
            Mark(mb > 0 ? $"mood Calm -> Battle {mb - s9:0.0} s after the skirmish began (alive {Alive(sk)})" : "NO mood Calm -> Battle within 60 s");
            if (mb > 0) { yield return Wait(1.5f); Snap("battle mood"); yield return Frame("L02_skirmish_battle_mood", Mid(sk, red)); }
            yield return Wait(Mathf.Max(0f, 14f - (T - s9)));
            Kill(sk); float k9 = T; Mark("skirmish over: the rest of both squads removed");
            yield return WaitLine("mood Battle -> Calm", k9, 90f);
            float bc = FirstSince("mood Battle -> Calm", k9);
            Verdict("step 9 (mood switch)", mb > 0 && bc > 0, mb > 0 && bc > 0 ? $"{LineSince("mood Calm -> Battle", s9)}  ...  {LineSince("mood Battle -> Calm", k9)} ({bc - k9:0.0} s after the fight)" : "missing mood line(s)");
            yield return Wait(4f);

            // ---- step 6: a REAL attack on THEIR fort (spawned red units, the player's right-click order on its guns), step 12, step 8
            double dx = ourFort.CenterX - theirFort.CenterX, dy = ourFort.CenterY - theirFort.CenterY, dl = Math.Sqrt(dx * dx + dy * dy); dx /= dl; dy /= dl;
            int ax = theirFort.CenterX + (int)(dx * 19 * 16), ay = theirFort.CenterY + (int)(dy * 19 * 16);
            yield return RealAttack("L02", theirFort, ax, ay, dx, dy, true);
            if (done) yield break;

            // ---- the win: the end screen, the end music, then music off / on on the end screen (step 12)
            yield return Wait(3f);
            float g0 = T; Mark("DebugFortFalls(true): " + OpenZVictory.DebugFortFalls(true));
            yield return WaitLine("END SCREEN", g0, 100f);
            float es = FirstSince("END SCREEN", g0);
            yield return WaitLine("(end music)", g0, 20f);
            float em = FirstSince("(end music)", g0);
            Mark(es > 0 ? $"END SCREEN {es - g0:0.0} s after the fall, end music {(em > 0 ? (em - es).ToString("0.0") + " s later" : "NOT started")}" : "NO END SCREEN within 100 s");
            yield return Wait(3f); Snap("end music"); yield return Frame("L02_endscreen_end_music");
            float o1 = T; AudioDirector.MusicEnabled = false; Mark("step 12 (end screen): music off");
            yield return WaitLine("music off (oz_music)", o1, 5f); yield return Wait(2f); Snap("end screen, music off");
            float o2 = T; AudioDirector.MusicEnabled = true; Mark("step 12 (end screen): music on");
            yield return WaitLine("music on:", o2, 5f); yield return Wait(3f); Snap("end screen, music on");
            string onLine = LineSince("music on:", o2);
            Verdict("step 12 (end screen off / on)", SeenSince("music off (oz_music)", o1) && onLine != null && onLine.Contains("RESUMES") && onLine.Contains("(end music)"), $"{LineSince("music off (oz_music)", o1)}  ...  {onLine}");
            Verdict("win + end music", es > 0 && em > 0 && CountSince("(end music)", g0) == 1, $"END SCREEN {(es > 0 ? "yes" : "no")}, end-music starts {CountSince("(end music)", g0)}");
        }
        static readonly Regex rxCalm = new Regex(@"calm ([\d.]+)/(\d+) s");
        static float Calm() { var ad = AudioDirector.I; var m = ad ? rxCalm.Match(ad.MusicV3Status()) : null; return m != null && m.Success ? float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : -1f; }
        int CountSince(string needle, float since) { int n = 0; foreach (var l in lines) if (l.t >= since && l.s.Contains(needle) && l.s.Contains("start v")) n++; return n; }

        /// <summary>Spawn red attackers ~19 tiles out, order them onto the enemy fort's guns (PlayerInput.OrderFor = the right-click order),
        /// wait for the drama, [L02: music off / on], pull back 30 tiles out, [L02: the pause menu once the fort is quiet], the fade back
        /// and the resume check.</summary>
        IEnumerator RealAttack(string lv, ZObject fort, int ax, int ay, double dx, double dy, bool extras)
        {
            var ad = AudioDirector.I; Team them = fort.Owner;
            // heavies shot the fort's only gun and blue's fort fell 4 s into the drama. A harmless, unkillable blue squad parked
            // 12 tiles to the side keeps the match alive for the proof (never near a fort reading, never shooting).
            ZObject keeper = null;
            foreach (var (kx, ky) in new[] { (fort.CenterX - (int)(dy * 12 * 16), fort.CenterY + (int)(dx * 12 * 16)), (fort.CenterX + (int)(dy * 12 * 16), fort.CenterY - (int)(dx * 12 * 16)), (fort.CenterX - (int)(dx * 8 * 16), fort.CenterY - (int)(dy * 8 * 16)) })
            {
                if (!OzCheatSpawner.FreeTile(W, kx >> 4, ky >> 4, 1, true, out _, out _)) continue;
                keeper = Squad("grunt", them, kx, ky);
                foreach (var m in new List<ZObject>(keeper.Minions) { keeper }) { m.MaxHealth = 1000000000; m.SetHealth(m.MaxHealth); m.Damage = 0; m.MoveSpeed = 0; m.RealMoveSpeed = 0; }
                break;
            }
            Mark(keeper != null ? $"keeper: a harmless unkillable blue grunt squad at ({keeper.CenterX >> 4},{keeper.CenterY >> 4}) tiles, {D(keeper, fort.CenterX, fort.CenterY) / 16:0} tiles from their fort (keeps blue in the match)" : "NO keeper placed");
            var targets = new List<ZObject>();
            foreach (var o in W.Objects) if (o.Kind == ZKind.Cannon && o.Owner == them && !o.Destroyed && D(o, fort.CenterX, fort.CenterY) <= ad.musicV3Config.rCore) targets.Add(o);
            targets.Sort((p, q) => D(p, ax, ay).CompareTo(D(q, ax, ay)));
            var atk = new List<ZObject>();
            var s1 = Squad("grunt", g.player, ax + (int)(dy * 40), ay - (int)(dx * 40)); var s2 = Squad("grunt", g.player, ax - (int)(dy * 40), ay + (int)(dx * 40));
            var h1 = Vehicle("heavy", g.player, ax + (int)(dx * 40) + (int)(dy * 72), ay + (int)(dy * 40) - (int)(dx * 72));
            var h2 = Vehicle("heavy", g.player, ax + (int)(dx * 40) - (int)(dy * 72), ay + (int)(dy * 40) + (int)(dx * 72));
            var md = Vehicle("medium", g.player, ax + (int)(dx * 56), ay + (int)(dy * 56));
            var leaders = new List<ZObject> { s1, s2, h1, h2, md };
            atk.AddRange(leaders); atk.AddRange(s1.Minions); atk.AddRange(s2.Minions);
            float r0 = T; int ok = 0;
            for (int i = 0; i < leaders.Count; i++)
            {
                var t = targets.Count > 0 ? targets[i % Math.Min(2, targets.Count)] : fort;
                bool sentOk = t == fort && leaders[i].IsRobot ? Order(leaders[i], null, fort.CenterX + (int)(dx * 9 * 16), fort.CenterY + (int)(dy * 9 * 16)) : Order(leaders[i], t, t.CenterX, t.CenterY);
                if (sentOk) ok++;
            }
            Mark($"step 6 ({lv}): {atk.Count} red units (2 grunt squads, 2 heavy tanks, a medium tank) {D(s1, fort.CenterX, fort.CenterY) / 16:0} tiles from THEIR fort; {ok}/{leaders.Count} right-click orders onto {(targets.Count > 0 ? string.Join(", ", targets.GetRange(0, Math.Min(2, targets.Count)).ConvertAll(x => x.Type + "#" + x.Id)) : "the fort (no guns in its area)")}; reading now theirs {Theirs()}");
            float w = 0f, nextRead = 0f;
            while (!SeenSince("BIG ATTACK on their fort", r0) && w < 75f && !done)
            { if (w >= nextRead) { nextRead += 2f; log.AppendLine($"[{T,7:0.00}]    reading theirs {Theirs()}, attackers alive {Alive(atk)}/{atk.Count}"); } w += Time.unscaledDeltaTime; yield return null; }
            float big = FirstSince("BIG ATTACK on their fort", r0);
            string bigLine = LineSince("BIG ATTACK on their fort", r0);
            Mark(big > 0 ? $"BIG ATTACK {big - r0:0.0} s after the orders (alive {Alive(atk)}/{atk.Count}): {bigLine}" : $"NO BIG ATTACK within 75 s (alive {Alive(atk)}, theirs {Theirs()})");
            if (big < 0) { Verdict($"step 6 ({lv}, real attack)", false, "no drama from the real attack"); Kill(atk); yield break; }
            yield return WaitLine("(drama)", big, 6f); yield return Wait(2.5f); Snap("real attack drama");
            yield return Frame(lv + "_real_attack_drama", Mid(atk, targets.Count > 0 ? targets[0] : fort));
            float dramaAt = big;

            if (extras)
            {   // step 12 during the drama: music off, then on (the drama resumes, the attack goes on)
                yield return Wait(1f);
                float o1 = T; AudioDirector.MusicEnabled = false; Mark("step 12: music off during the drama");
                yield return WaitLine("music off (oz_music)", o1, 5f); yield return Wait(1.5f); Snap("music off in the drama");
                string lpOff = Lp(); yield return Frame("L02_music_off_in_drama", Mid(atk, fort));
                yield return Wait(2f);
                float o2 = T; AudioDirector.MusicEnabled = true; Mark("step 12: music on during the drama");
                yield return WaitLine("music on:", o2, 5f); yield return Wait(2f); Snap("music on in the drama");
                string on = LineSince("music on:", o2);
                bool silent = !Regex.IsMatch(lpOff, @"vol 0\.(0[1-9]|[1-9])");
                Verdict("step 12 (off / on in the drama)", SeenSince("music off (oz_music) in Drama", o1) && silent && on != null && on.Contains("the attack goes on"),
                        $"{LineSince("music off (oz_music)", o1)}; 1.5 s later {lpOff}; then {on}");
            }

            // pull back: every live attacker 30 tiles out (the player's right-click on the ground)
            yield return Wait(Mathf.Max(0f, 16f - (T - dramaAt)));
            int bx = fort.CenterX + (int)(dx * 30 * 16), by = fort.CenterY + (int)(dy * 30 * 16); int pulled = 0;
            foreach (var u in leaders) if (!u.Destroyed && Order(u, null, bx, by)) pulled++;
            float p0 = T; Mark($"pull back: {pulled} leaders ordered 30 tiles out (alive {Alive(atk)}), reading theirs {Theirs()}");
            w = 0f; FortReading rd = Theirs();
            while (!(rd.units == 0 && rd.sinceHit > 1.5f) && w < 45f && !done) { w += Time.unscaledDeltaTime; yield return null; rd = Theirs(); }
            bool killed = false;
            if (!(rd.units == 0 && rd.sinceHit > 1.5f)) { killed = true; Kill(atk); Mark($"the pull back did not clear the fort in 45 s (theirs {rd}): the remaining attackers removed"); yield return Wait(2f); rd = Theirs(); }
            float quietAt = T; double simQuiet = W.Time;
            Mark($"fort quiet {quietAt - p0:0.0} s after the pull back (theirs {rd}, alive {Alive(atk)}){(killed ? " after removing them" : "")}; no attack over yet: {!SeenSince("attack over", r0)}");

            float unpaused = -1f;
            if (extras)
            {   // step 8: the pause menu, 30 s, during the drama with the fort already quiet
                var pm = FindFirstObjectByType<OpenZPauseMenu>(FindObjectsInactive.Include);
                float q0 = T; double simP0 = W.Time;
                if (pm) pm.Open(); else g.paused = true;
                Mark("step 8: " + (pm ? "the pause menu opened (Esc)" : "ZGame.paused = true (no pause menu found)"));
                yield return Wait(0.6f); float calm0 = Calm(); string st0 = ad.MusicV3Status();
                yield return Wait(2.4f); Snap("paused 3 s"); string lpP = Lp(); yield return Frame("L02_pause_menu_drama");
                yield return Wait(Mathf.Max(0f, 30f - (T - q0)));
                Snap("paused 30 s"); string lpP30 = Lp(); float calm30 = Calm();
                bool noOver = !SeenSince("attack over", q0); double simPaused = W.Time - simP0;
                if (pm) pm.Close(); else g.paused = false;
                unpaused = T; Mark($"step 8: unpaused after {unpaused - q0:0.0} s (sim clock moved {simPaused:0.00} s while paused)");
                var rxOn = new Regex(@"lp ON 900 Hz vol 0\.\d*[1-9]\d* playing");
                bool lpOn = rxOn.IsMatch(lpP) && rxOn.IsMatch(lpP30);
                bool frozen = calm0 >= 0f && Mathf.Abs(calm30 - calm0) <= 0.3f;
                Verdict("step 8 (pause)", noOver && lpOn && frozen && simPaused < 0.1, $"no attack over in the 30 s pause: {noOver}; the detector's calm count stood at {calm0:0.0} s -> {calm30:0.0} s over the pause; the drama kept playing low-passed: {lpOn} ({lpP30}); the sim clock moved {simPaused:0.00} s");
            }

            yield return WaitLine("attack over", r0, 70f);
            float over = FirstSince("attack over", r0);
            yield return WaitLine("resume check", over > 0 ? over : r0, 12f);
            float rc = FirstSince("resume check", over > 0 ? over : r0);
            string rcl = rc > 0 ? LineSince("resume check", over > 0 ? over : r0) : null;
            string rs = LineSince("RESUMES at", over > 0 ? over : r0);
            Mark(over > 0 ? $"attack over {over - quietAt:0.0} s after the fort went quiet{(unpaused > 0 ? $" ({over - unpaused:0.0} s after the unpause)" : "")}: {LineSince("attack over", r0)}" : "NO attack over within 70 s");
            Snap("resumed"); yield return Frame(lv + "_real_attack_resumed", ZGame.PxToWorld(fort.CenterX + dx * 12 * 16, fort.CenterY + dy * 12 * 16));
            bool calmAfterPause = unpaused < 0 || over > unpaused;
            Verdict($"step 6 ({lv}, real attack)", over > 0 && rcl != null && rcl.Contains("-> OK") && rs != null && calmAfterPause,
                $"{bigLine}  ...  {rs}  ...  {rcl}{(unpaused > 0 ? $"; the calm count ran only after the unpause ({over - unpaused:0.0} s)" : "")}");
            Kill(atk);
        }

        IEnumerator L02B()
        {
            phase = "L02";
            float w = 0f; while ((!AudioDirector.I || AudioDirector.I.MusicV3Core == null || !ZGame.I || ZGame.I.World == null) && w < 240f && !done) { w += Time.unscaledDeltaTime; yield return null; }
            LevelSetup("L02"); Record("L02B"); yield return Wait(5f); Snap("L02 start");
            Team me = g.player, them = me == Team.Red ? Team.Blue : Team.Red; var ourFort = Fort(me); var theirFort = Fort(them);
            double dx = ourFort.CenterX - theirFort.CenterX, dy = ourFort.CenterY - theirFort.CenterY, dl = Math.Sqrt(dx * dx + dy * dy); dx /= dl; dy /= dl;
            yield return RealAttack("L02", theirFort, theirFort.CenterX + (int)(dx * 13 * 16), theirFort.CenterY + (int)(dy * 13 * 16), dx, dy, true);
        }

        IEnumerator L17()
        {
            phase = "L17";
            float w = 0f; while ((!AudioDirector.I || AudioDirector.I.MusicV3Core == null || !ZGame.I || ZGame.I.World == null) && w < 240f && !done) { w += Time.unscaledDeltaTime; yield return null; }
            LevelSetup("L17"); Record("L17");
            var ad = AudioDirector.I; Team me = g.player, them = me == Team.Red ? Team.Blue : Team.Red;
            var ourFort = Fort(me); var theirFort = Fort(them);
            // step 12: a new level with oz_music 0
            yield return Wait(12f); Snap("oz_music 0, 12 s in");
            bool offLive = SeenSince("music OFF (oz_music)", 0f), noStart = !SeenSince("start v", 0f);
            Verdict("step 12 (new level with oz_music 0)", offLive && noStart, $"{LineSince("[MusicV3] live:", 0f)}; start v lines: {(noStart ? "none" : "SOME")}; {Lp()}");
            float m1 = T; AudioDirector.MusicEnabled = true; Mark("music on");
            yield return WaitLine("start v", m1, 8f); yield return Wait(6f); Snap("music on");

            float a0 = T; ad.MusicV3Force(2, 20f); Mark("Force big attack on the ENEMY fort 20 s");
            yield return WaitLine("BIG ATTACK", a0, 12f); float big = FirstSince("BIG ATTACK", a0);
            yield return Wait(4.5f); Snap("forced drama"); yield return Frame("L17_forced_drama", ZGame.PxToWorld(theirFort.CenterX, theirFort.CenterY));
            yield return WaitLine("resume check", a0, 75f);
            float rc = FirstSince("resume check", a0); string rcl = LineSince("resume check", a0);
            Snap("forced cycle resumed"); yield return Frame("L17_forced_resumed");
            Verdict("L17 resume check (forced cycle, 50 ms tolerance)", big > 0 && rcl != null && rcl.Contains("-> OK"), $"BIG ATTACK {(big > 0 ? (big - a0).ToString("0.0") + " s after the force" : "none")}; {LineSince("ToRegular", a0)}; {rcl}");
            float back = FirstSince("ToRegular", a0);

            // step 6 on L17: a real attack once the 45 s cooldown after that fade back has passed
            double dx = ourFort.CenterX - theirFort.CenterX, dy = ourFort.CenterY - theirFort.CenterY, dl = Math.Sqrt(dx * dx + dy * dy); dx /= dl; dy /= dl;
            int ax = theirFort.CenterX + (int)(dx * 13 * 16), ay = theirFort.CenterY + (int)(dy * 13 * 16);
            if (back > 0) yield return Wait(Mathf.Max(0f, back + 40f - T));
            Mark($"{(back > 0 ? T - back : -1f):0.0} s after the fade back: the real attack");
            yield return RealAttack("L17", theirFort, ax, ay, dx, dy, false);
        }

        // OpenZ > Music v3 > Status + Skip through a calm, a battle, a fort-danger and a VICTORY song on L17, no back-to-back repeats.
        // Every earlier L17 run ended in a loss (end music = a menu song); this run WINS on L17 (DebugFortFalls(true), as L02's win).
        static readonly FieldInfo fReg = typeof(MusicCore).GetField("reg", BindingFlags.NonPublic | BindingFlags.Instance),
            fDrama = typeof(MusicCore).GetField("drama", BindingFlags.NonPublic | BindingFlags.Instance),
            fEnd = typeof(MusicCore).GetField("endS", BindingFlags.NonPublic | BindingFlags.Instance);
        static MusicTrack SlotTrack(FieldInfo f)
        {
            var ad = AudioDirector.I; if (!ad || ad.MusicV3Core == null || f == null) return null;
            var s = f.GetValue(ad.MusicV3Core); if (s == null) return null;
            var ft = s.GetType().GetField("track", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return ft != null ? ft.GetValue(s) as MusicTrack : null;
        }
        static string TrackDesc(MusicTrack t) { return t == null ? "none" : t.name + " [" + t.pool + "]"; }
        const string MStatus = "OpenZ/Music v3/Status (Play mode)", MSkip = "OpenZ/Music v3/Skip regular song (Play mode)", MOurs = "OpenZ/Music v3/Force big attack on OUR fort 20 s (Play mode)";
        bool Menu(string item) { bool ok = false; try { ok = UnityEditor.EditorApplication.ExecuteMenuItem(item); } catch (Exception e) { log.AppendLine("menu failed " + e.Message); } Mark("menu OpenZ > " + item.Substring(6).Replace("/", " > ") + (ok ? "" : " FAILED")); return ok; }
        static readonly Regex rxStart = new Regex(@"start v\d (\S+) @");
        List<string> StartsSince(float since)
        {
            var r = new List<string>();
            foreach (var l in lines) if (l.t >= since && l.s.Contains(" start v") && !l.s.Contains("back where it was")) { var m = rxStart.Match(l.s); if (m.Success) r.Add(m.Groups[1].Value); }
            return r;
        }

        IEnumerator L17W()
        {
            phase = "L17";
            float w = 0f; while ((!AudioDirector.I || AudioDirector.I.MusicV3Core == null || !ZGame.I || ZGame.I.World == null) && w < 240f && !done) { w += Time.unscaledDeltaTime; yield return null; }
            w = 0f; while (FindFirstObjectByType<TestModeRuntime>() == null && GameBoot.TestMode && w < 5f) { w += Time.unscaledDeltaTime; yield return null; }
            LevelSetup("L17"); Record("L17W");
            var ad = AudioDirector.I; var core = ad.MusicV3Core; Team me = g.player, them = me == Team.Red ? Team.Blue : Team.Red;
            var ourFort = Fort(me); var theirFort = Fort(them);
            if (ourFort == null || theirFort == null) { Verdict("setup", false, "forts not found"); yield break; }
            yield return WaitLine("start v", 0f, 20f); yield return Wait(6f);

            // 1 a calm song, read with OpenZ > Music v3 > Status
            bool st1 = Menu(MStatus); var calm1 = SlotTrack(fReg); Snap("calm (Status)");
            yield return Frame("L17W_1_calm_status");
            Verdict("1 calm song (Status)", st1 && calm1 != null && calm1.pool == MusicPool.Calm && core.Layer == MusicLayer.Regular, $"layer {core.Layer}, regular song {TrackDesc(calm1)}");

            // 2 Skip (README step 2, never run on L17 before): a new song of the same mood, never the same one
            float k0 = T; bool sk = Menu(MSkip);
            yield return WaitLine("start v", k0, 6f); yield return Wait(4f);
            var calm2 = SlotTrack(fReg); Menu(MStatus); Snap("after Skip");
            yield return Frame("L17W_2_calm_after_skip");
            Verdict("2 Skip (README step 2 on L17)", sk && calm1 != null && calm2 != null && calm2.name != calm1.name && calm2.pool == calm1.pool, $"{TrackDesc(calm1)} -> {TrackDesc(calm2)}; {LineSince("start v", k0)}");

            // 3 a battle song: a REAL skirmish away from both forts (spawned grunt squads, the player's order) -> mood Calm -> Battle
            int mx = (ourFort.CenterX + theirFort.CenterX) / 2, my = (ourFort.CenterY + theirFort.CenterY) / 2;
            float s9 = T; var red = Squad("grunt", me, mx - 40, my); var blue = Squad("grunt", them, mx + 40, my);
            var sk9 = new List<ZObject> { red, blue }; sk9.AddRange(red.Minions); sk9.AddRange(blue.Minions);
            foreach (var o in sk9) { o.MaxHealth = 1000000000; o.SetHealth(o.MaxHealth); }
            bool sent = Order(red, blue, blue.CenterX, blue.CenterY);
            Mark($"3: red grunt squad ({1 + red.Minions.Count}) + blue grunt squad ({1 + blue.Minions.Count}) at ({red.CenterX >> 4},{red.CenterY >> 4}) / ({blue.CenterX >> 4},{blue.CenterY >> 4}) tiles, {D(red, ourFort.CenterX, ourFort.CenterY) / 16:0} / {D(blue, theirFort.CenterX, theirFort.CenterY) / 16:0} tiles from the forts; red ordered onto blue: {sent}");
            yield return WaitLine("mood Calm -> Battle", s9, 60f);
            float mb = FirstSince("mood Calm -> Battle", s9);
            Mark(mb > 0 ? $"mood Calm -> Battle {mb - s9:0.0} s after the skirmish began (alive {Alive(sk9)})" : "NO mood Calm -> Battle within 60 s");
            yield return Wait(3f); var bat = SlotTrack(fReg); Menu(MStatus); Snap("battle mood (Status)");
            yield return Frame("L17W_3_battle_mood", Mid(sk9, red));
            bool dramaInSkirmish = SeenSince("BIG ATTACK", s9);
            Verdict("3 battle song", mb > 0 && bat != null && bat.pool == MusicPool.Battle && !dramaInSkirmish, $"{LineSince("mood Calm -> Battle", s9)}; regular song {TrackDesc(bat)}; drama during the skirmish: {(dramaInSkirmish ? "YES" : "no")}");
            yield return Wait(Mathf.Max(0f, 14f - (T - s9)));
            Kill(sk9); float k9 = T; Mark("skirmish over: both squads removed");
            yield return WaitLine("mood Battle -> Calm", k9, 30f);
            Mark(SeenSince("mood Battle -> Calm", k9) ? $"{LineSince("mood Battle -> Calm", k9)} ({FirstSince("mood Battle -> Calm", k9) - k9:0.0} s after the fight)" : "no mood Battle -> Calm within 30 s (the fort attack comes next anyway)");

            // 4 a fort-danger song: a big attack on OUR fort (OpenZ > Music v3 > Force big attack on OUR fort 20 s) -> the drama
            yield return Wait(2f);
            float a0 = T; bool fo = Menu(MOurs);
            yield return WaitLine("BIG ATTACK", a0, 12f); float big = FirstSince("BIG ATTACK", a0);
            yield return Wait(4.5f); var dr = SlotTrack(fDrama); Menu(MStatus); Snap("fort danger (Status)");
            yield return Frame("L17W_4_fort_danger_drama", ZGame.PxToWorld(ourFort.CenterX, ourFort.CenterY));
            Verdict("4 fort-danger song", fo && big > 0 && dr != null && dr.pool == MusicPool.Drama && core.Layer == MusicLayer.Drama, $"{(big > 0 ? $"BIG ATTACK {big - a0:0.0} s after the force" : "no BIG ATTACK")}; layer {core.Layer}; drama song {TrackDesc(dr)}; {LineSince("(drama", a0)}");

            // 5 a VICTORY song: THEIR fort falls during the drama (OpenZVictory.DebugFortFalls(true), as L02's win) -> drama to the end screen, then the end music
            yield return Wait(3f);
            float g0 = T; Mark("5: DebugFortFalls(true) on L17: " + OpenZVictory.DebugFortFalls(true));
            yield return WaitLine("END SCREEN", g0, 100f); float es = FirstSince("END SCREEN", g0); string esl = LineSince("END SCREEN", g0);
            yield return WaitLine("(end music)", g0, 20f); float em = FirstSince("(end music)", g0);
            Mark(es > 0 ? $"END SCREEN {es - g0:0.0} s after the fall, end music {(em > 0 ? (em - es).ToString("0.0") + " s later" : "NOT started")}; drama held to the end screen: {(SeenSince("game over (Drama)", g0) ? "yes" : "NO")}" : "NO END SCREEN within 100 s");
            yield return Wait(3f); var vic = SlotTrack(fEnd); Menu(MStatus); Snap("victory end music (Status)");
            yield return Frame("L17W_5_victory_endscreen");
            Verdict("5 victory song (L17 WIN)", es > 0 && esl != null && esl.Contains("(win)") && em > 0 && vic != null && vic.pool == MusicPool.Victory && CountSince("(end music)", g0) == 1,
                $"{esl}; {LineSince("(end music)", g0)}; end slot {TrackDesc(vic)}; end-music starts {CountSince("(end music)", g0)}; drama held: {(SeenSince("game over (Drama)", g0) ? "yes" : "NO")}");

            // 6 no back-to-back repeats over the whole run
            var seq = StartsSince(0f); int rep = 0; for (int i = 1; i < seq.Count; i++) if (seq[i] == seq[i - 1]) rep++;
            Verdict("6 no back-to-back repeats", rep == 0 && core.BackToBack == 0, $"song starts in order: {string.Join(" > ", seq.ToArray())}; MusicCore back-to-back {core.BackToBack}, starts {core.Starts}");
            yield return Wait(4f); Snap("end");
        }

        void Finish(string why)
        {
            if (done) return; done = true;
            try
            {
                if (tap) { tap.Close(); tap = null; }
                Mark(why == null ? "DONE" : "STOPPED: " + why);
                if (menuWindow.a > 0) MenuSummary();
                log.AppendLine($"errors/exceptions in the Console during the run: {errors}");
                log.AppendLine("## VERDICTS"); foreach (var v in verdicts) log.AppendLine("  " + v);
                log.AppendLine("## frames: " + string.Join(", ", shots));
                var ad = AudioDirector.I;
                if (ad && ad.MusicV3Core != null) { log.AppendLine("## MusicCore history (this level)"); foreach (var h in ad.MusicV3Core.History) log.AppendLine("  " + h); }
                string lv = job == "MENU_L02" ? "L02" : job;
                File.WriteAllText(Path.Combine(Dir, lv + "_fix_log.txt"), log.ToString());
                File.WriteAllText(Path.Combine(Dir, lv + "_fix_timeline.csv"), csv.ToString());
                PlayerPrefs.SetInt("oz_testmode", 0); PlayerPrefs.SetInt("oz_music", 1); PlayerPrefs.Save();
            }
            catch (Exception e) { Debug.LogError("[mv3fix] finish: " + e); }
            UnityEditor.EditorApplication.isPlaying = false;
        }

        /// <summary>Step 0 numbers from the 0.1 s samples: the deepest duck under Zod, how long it eased in, how long it took back.</summary>
        void MenuSummary()
        {
            var rows = csv.ToString().Split('\n'); float minDuck = 0f, tIn = -1f, tOut = -1f, minShared = 0f;
            foreach (var r in rows)
            {
                var c = r.Split(','); if (c.Length < 9 || c[1] != "menu") continue;
                if (!float.TryParse(c[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float t)) continue;
                float.TryParse(c[5], NumberStyles.Float, CultureInfo.InvariantCulture, out float d); float.TryParse(c[6], NumberStyles.Float, CultureInfo.InvariantCulture, out float sh);
                if (t >= menuWindow.a && t <= menuWindow.b + 0.2f) { minDuck = Mathf.Min(minDuck, d); minShared = Mathf.Min(minShared, sh); if (tIn < 0f && d <= -2.4f) tIn = t - menuWindow.a; }
                if (t > menuWindow.b && tOut < 0f && d >= -0.1f) tOut = t - menuWindow.b;
            }
            Verdict("step 0 (menu duck)", minDuck <= -2.3f && minDuck >= -2.7f && minShared > -1f && tOut > 0.8f && tOut < 3f,
                $"under Zod's greeting the Music bus dips {minDuck:0.00} dB (shared duck {minShared:0.00} dB: the old -8 dB drop is gone), reaches -2.4 dB {tIn:0.0} s after his first word, back to -0.1 dB {tOut:0.0} s after his last");
        }
    }
}
#endif
