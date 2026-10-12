using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OpenZ
{
    /// <summary>
    /// audio_v2: BS.1770 loudness meter on the AudioListener rig, AFTER LegacyMixCompressor (component order = filter order), so it reads
    /// what the player hears: momentary (400 ms), short-term (3 s), integrated (gated, since Reset), max momentary, sample peak, plus the
    /// - Ctrl+Shift+F9 toggles a small read-out (top left). Command line -audiometer logs a line every 10 s to the player log.
    /// - Probe(): the Play-mode level check (menu OpenZ/Audio/audio_v2 Level Probe, or -audioprobe in a build): freezes the sim
    ///   (timeScale 0), puts the camera at the default 70 m view, fires one sound per category at the screen centre and compares the
    /// The audio thread only sums K-weighted energy into 100 ms blocks; everything else runs on the main thread.
    /// </summary>
    public class OzLoudnessMeter : MonoBehaviour
    {
        public static OzLoudnessMeter I { get; private set; }
        public bool showOverlay;
        [Tooltip("Log a loudness line every N s to the player log (0 = off; -audiometer sets 10)")] public float logEvery;

        // ---- audio thread
        int rate; double b0s, b1s, b2s, a1s, a2s, b0h, b1h, b2h, a1h, a2h;
        readonly double[] st = new double[16];                        // per channel (max 4): shelf x1 x2 y1 y2 (high-pass state: hx / hy)
        double acc; int accN, blockN; float peakAbs;
        readonly object gate = new object();
        readonly Queue<(double e, float pk)> blocks = new Queue<(double, float)>();

        // ---- main thread
        readonly List<double> win = new List<double>();                // 100 ms block energies, last 30
        readonly List<double> gatedM = new List<double>();             // momentary energies since Reset (integrated)
        public float Momentary { get; private set; } = -120f;
        public float ShortTerm { get; private set; } = -120f;
        public float Integrated { get; private set; } = -120f;
        public float MaxMomentary { get; private set; } = -120f;
        public float PeakDb { get; private set; } = -120f;
        /// <summary>Max momentary since the last MarkWindow() (the probe's event window).</summary>
        public float WindowMaxM { get; private set; } = -120f;
        public float WindowMaxGr { get; private set; }
        readonly List<float> windowM = new List<float>();
        float nextLog, nextIntegrated;
        LegacyMixCompressor comp;

        void Awake()
        {
            I = this;
            rate = AudioSettings.outputSampleRate;
            Design(rate);
            foreach (var a in System.Environment.GetCommandLineArgs())
            {
                if (!global::OpenZ.OzRelease.IsRelease && a == "-audiometer" && logEvery <= 0f) logEvery = 10f;
                if (!global::OpenZ.OzRelease.IsRelease && a == "-audioprobe") StartCoroutine(ProbeWhenReady());
            }
        }
        void OnDestroy() { if (I == this) I = null; }

        void Design(int sr)
        {   // loudness.py: RBJ high shelf (G 3.9998 dB, Q .7072, fc 1681.97 Hz) + RBJ high pass (Q .5003, fc 38.135 Hz)
            if (sr <= 0) sr = 48000;
            double A = System.Math.Pow(10, 3.999843853973347 / 40), w0 = 2 * System.Math.PI * 1681.974450955533 / sr, al = System.Math.Sin(w0) / (2 * 0.7071752369554196), c = System.Math.Cos(w0), sA = System.Math.Sqrt(A);
            double a0 = (A + 1) - (A - 1) * c + 2 * sA * al;
            b0s = A * ((A + 1) + (A - 1) * c + 2 * sA * al) / a0; b1s = -2 * A * ((A - 1) + (A + 1) * c) / a0; b2s = A * ((A + 1) + (A - 1) * c - 2 * sA * al) / a0;
            a1s = 2 * ((A - 1) - (A + 1) * c) / a0; a2s = ((A + 1) - (A - 1) * c - 2 * sA * al) / a0;
            w0 = 2 * System.Math.PI * 38.13547087602444 / sr; al = System.Math.Sin(w0) / (2 * 0.5003270373238773); c = System.Math.Cos(w0);
            a0 = 1 + al;
            b0h = (1 + c) / 2 / a0; b1h = -(1 + c) / a0; b2h = (1 + c) / 2 / a0; a1h = -2 * c / a0; a2h = (1 - al) / a0;
            blockN = Mathf.Max(1, sr / 10);
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            int ch = Mathf.Min(channels, 4);
            for (int i = 0; i < data.Length; i += channels)
            {
                double e = 0;
                for (int k = 0; k < ch; k++)
                {
                    float xf = data[i + k]; float ax = xf < 0 ? -xf : xf; if (ax > peakAbs) peakAbs = ax;
                    int s = k * 4; double x = xf;
                    double y = b0s * x + b1s * st[s] + b2s * st[s + 1] - a1s * st[s + 2] - a2s * st[s + 3];
                    st[s + 1] = st[s]; st[s] = x; st[s + 3] = st[s + 2]; st[s + 2] = y;
                    double z = b0h * y + b1h * hx1[k] + b2h * hx2[k] - a1h * hy1[k] - a2h * hy2[k];
                    hx2[k] = hx1[k]; hx1[k] = y; hy2[k] = hy1[k]; hy1[k] = z;
                    e += z * z;
                }
                acc += e;
                if (++accN >= blockN)
                {
                    lock (gate) { blocks.Enqueue((acc / accN, peakAbs)); while (blocks.Count > 200) blocks.Dequeue(); }
                    acc = 0; accN = 0; peakAbs = 0f;
                }
            }
        }
        readonly double[] hx1 = new double[4], hx2 = new double[4], hy1 = new double[4], hy2 = new double[4];

        static float Lufs(double ms) => (float)(-0.691 + 10.0 * System.Math.Log10(System.Math.Max(ms, 1e-20)));

        void Update()
        {
            if (!comp) comp = GetComponent<LegacyMixCompressor>();
            var kb = Keyboard.current;
            if (kb != null && !global::OpenZ.OzRelease.IsRelease && kb.ctrlKey.isPressed && kb.shiftKey.isPressed && kb.f9Key.wasPressedThisFrame) showOverlay = !showOverlay;
            while (true)
            {
                (double e, float pk) b;
                lock (gate) { if (blocks.Count == 0) break; b = blocks.Dequeue(); }
                win.Add(b.e); if (win.Count > 30) win.RemoveAt(0);
                PeakDb = b.pk > 1e-6f ? 20f * Mathf.Log10(b.pk) : -120f;
                if (win.Count >= 4)
                {
                    double m = 0; for (int i = win.Count - 4; i < win.Count; i++) m += win[i]; m /= 4;
                    Momentary = Lufs(m);
                    if (Momentary > MaxMomentary) MaxMomentary = Momentary;
                    if (Momentary > WindowMaxM) WindowMaxM = Momentary;
                    windowM.Add(Momentary);
                    if (Momentary > -70f) { gatedM.Add(m); if (gatedM.Count > 72000) gatedM.RemoveAt(0); }
                }
                double s = 0; foreach (var x in win) s += x; ShortTerm = Lufs(s / win.Count);
            }
            if (comp && comp.GainReductionDb > WindowMaxGr) WindowMaxGr = comp.GainReductionDb;
            if (Time.unscaledTime >= nextIntegrated) { nextIntegrated = Time.unscaledTime + 1f; Integrated = GatedIntegrated(); }
            if (logEvery > 0f && Time.unscaledTime >= nextLog)
            {
                nextLog = Time.unscaledTime + logEvery;
                Debug.Log($"[OzLoudness] t {Time.unscaledTime:0} s  M {Momentary:0.0}  S {ShortTerm:0.0}  I {Integrated:0.0}  maxM {MaxMomentary:0.0} LUFS  peak {PeakDb:0.0} dBFS  GR {(comp ? comp.GainReductionDb : 0f):0.0} dB");
            }
        }

        float GatedIntegrated()
        {
            if (gatedM.Count == 0) return -120f;
            double sum = 0; foreach (var m in gatedM) sum += m;
            float rel = Lufs(sum / gatedM.Count) - 10f; double s2 = 0; int n = 0;
            foreach (var m in gatedM) if (Lufs(m) > rel) { s2 += m; n++; }
            return n > 0 ? Lufs(s2 / n) : -120f;
        }

        /// <summary>Clears max / integrated.</summary>
        public void ResetStats() { MaxMomentary = -120f; gatedM.Clear(); Integrated = -120f; MarkWindow(); }
        /// <summary>Starts a new event window (WindowMaxM, WindowMaxGr, window median).</summary>
        public void MarkWindow() { WindowMaxM = -120f; WindowMaxGr = 0f; windowM.Clear(); }
        public float WindowMedianM { get { if (windowM.Count == 0) return -120f; var c = new List<float>(windowM); c.Sort(); return c[c.Count / 2]; } }

        void OnGUI()
        {
            if (!showOverlay) return;
            var r = new Rect(12, 12, 520, 44);
            GUI.color = new Color(0, 0, 0, 0.7f); GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = Color.white;
            GUI.Label(new Rect(r.x + 8, r.y + 4, r.width - 16, 20), $"M {Momentary:0.0}   S {ShortTerm:0.0}   I {Integrated:0.0}   max M {MaxMomentary:0.0} LUFS   peak {PeakDb:0.0} dBFS");
            GUI.Label(new Rect(r.x + 8, r.y + 22, r.width - 16, 20), $"master GR {(comp ? comp.GainReductionDb : 0f):0.0} dB   ceiling {(comp ? comp.LimiterDb : 0f):0.0} dB   (Ctrl+Shift+F9 hides, audio_v2)");
        }

        // ================================================================================================================ probe
        /// <summary>audio_v2 targets (tools/probe_targets.json, pre-master, h70, screen centre, no duck). The master adds +4 dB make-up
        /// under its -16 dBFS threshold, so a reading is expected at target + 4 - (the master's gain reduction in that window).</summary>
        static readonly (string id, string what, float pre, float dur)[] Items =
        {
            ("alan", "Alan order line (vo_order_ack)", -19.7f, 2.2f), ("rachel", "Rachel line (ann_robot_manufactured)", -20.8f, 2.5f),
            ("ui_click", "UI click", -27.8f, 1f), ("ui_hover", "UI hover", -33.9f, 1f), ("ui_select_unit", "UI select", -28.9f, 1f), ("ui_error", "UI error", -28.2f, 1f),
            ("bolt", "grunt bolt shot (legacy)", -32.4f, 1.2f), ("heavy_tank_shot", "heavy tank shot (legacy)", -25.2f, 2f),
            ("explosion", "explosion (legacy bank)", -28.1f, 2.2f), ("vehicle_death", "vehicle death (legacy heavy bank)", -26.9f, 2.2f),
            ("flag_capture", "flag capture (legacy clip)", -28.6f, 1.4f), ("bld_door_open", "robot factory door open", -31.2f, 2.2f),
            ("ter_flag_raise", "flag raise", -29.9f, 1.6f), ("rob_step_sand", "robot footstep, sand", -35.0f, 0.8f),
            ("rob_squad_march", "squad march loop (integrated)", -34.1f, 3.2f), ("bld_factory_hum", "fan spin-up loop (integrated)", -36.2f, 3.2f),
            ("veh_tracks_loop", "tank tracks loop (integrated)", -35.0f, 3.2f), ("water", "river / harbour loop (integrated)", -37.1f, 3.2f),
            ("offscreen", "explosion 1 screen off to the right (distant battle)", -40.5f, 2.2f),
        };

        IEnumerator ProbeWhenReady()
        {
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 30f && (!ZGame.I || !AudioDirector.I || !CombatAudio.I)) yield return null;
            yield return new WaitForSecondsRealtime(6f);
            yield return Probe(null);
        }

        /// <summary></summary>
        public IEnumerator Probe(string reportPath)
        {
            var game = ZGame.I; var ad = AudioDirector.I; var ca = CombatAudio.I; var rts = FindFirstObjectByType<RtsCamera>();
            if (!game || !ad || !ca || !rts) { Debug.LogWarning("[OzLoudness] probe: needs a level in Play mode (ZGame, AudioDirector, CombatAudio, RtsCamera)"); yield break; }
            float scale = Time.timeScale, h0 = rts.height;
            Time.timeScale = 0f;                                                                    // the sim stops, the audio keeps running
            rts.SetHeightImmediate(70f);
            yield return new WaitForSecondsRealtime(2.5f);
            var f = rts.focus; var at = new Vector3(f.x, game.GroundY(f.x, f.z) + 1f, f.z);
            MarkWindow(); yield return new WaitForSecondsRealtime(3.2f);
            float bgMed = WindowMedianM, bgS = ShortTerm, bgMax = WindowMaxM;
            var sb = new StringBuilder();
            sb.AppendLine($"# audio_v2 level probe: {game.level}, {System.DateTime.Now:yyyy-MM-dd HH:mm}");
            sb.AppendLine();
            sb.AppendLine($"Default view 70 m, screen centre, sim frozen. Background (beds, idle engines, whatever else was playing): momentary median **{bgMed:0.0}**, "
                        + $"max {bgMax:0.0}, short-term {bgS:0.0} LUFS. Master make-up +{(GetComponent<LegacyMixCompressor>() ? GetComponent<LegacyMixCompressor>().makeupDb : 0f):0} dB.");
            sb.AppendLine();
            sb.AppendLine("| sound | target (pre-master) | expected here | measured max M | over background | master GR | verdict |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            int pass = 0, warn = 0, fail = 0;
            float makeup = GetComponent<LegacyMixCompressor>() ? GetComponent<LegacyMixCompressor>().makeupDb : 0f;
            foreach (var it in Items)
            {
                MarkWindow();
                string loopKey = null;
                Fire(it.id, at, rts, game, ad, ca, ref loopKey);
                yield return new WaitForSecondsRealtime(it.dur);
                if (loopKey != null) ad.StopLoop(loopKey, 0.1f);
                float m = WindowMaxM, gr = WindowMaxGr;
                float expect = it.pre + makeup - Mathf.Min(gr, 6f) * 0.5f;
                float over = m - bgMed, dev = m - expect;
                string verdict;
                bool quietLayer = it.pre <= -36f;
                if (it.id == "offscreen") verdict = over >= 1f ? "PASS (audible over the floor)" : "WARN (under the floor: listen)";
                else if (Mathf.Abs(dev) <= 3f && (quietLayer || over >= 4f)) verdict = "PASS";
                else if (Mathf.Abs(dev) <= 5f) verdict = "WARN";
                else verdict = "FAIL";
                if (verdict.StartsWith("PASS")) pass++; else if (verdict.StartsWith("WARN")) warn++; else fail++;
                sb.AppendLine($"| {it.what} | {it.pre:0.0} | {expect:0.0} | {m:0.0} | {over:+0.0;-0.0} | {gr:0.0} | {verdict} {(verdict == "PASS" ? "" : $"({dev:+0.0;-0.0} dB)")} |");
                yield return new WaitForSecondsRealtime(1.2f);
            }
            MarkWindow(); yield return new WaitForSecondsRealtime(3.2f);
            string bedId = game.level == "L17" ? "amb_l17_day / amb_l17_night_fog" : "amb_l02_day / dusk / night";
            sb.AppendLine($"| ambience bed ({bedId}) + floor, short-term | -38..-40 | {-38f + makeup:0.0}..{-40f + makeup:0.0} | S {ShortTerm:0.0} | - | {WindowMaxGr:0.0} | {(ShortTerm >= -46f + makeup && ShortTerm <= -30f + makeup ? "PASS" : "WARN")} |");
            sb.AppendLine();
            sb.AppendLine($"Result: {pass} PASS, {warn} WARN, {fail} FAIL. Tolerance +-3 dB on the expected reading (+-5 = WARN); one-shots and UI must also stand 4 dB over the background.");
            sb.AppendLine("Expected here = target + make-up - half the master's gain reduction in the window (the meter reads after the master).");
            rts.SetHeightImmediate(h0);
            Time.timeScale = scale;
            if (string.IsNullOrEmpty(reportPath)) reportPath = Path.Combine(QcDir(), $"audio_v2_levels_{game.level}_{System.DateTime.Now:yyyyMMdd_HHmm}.md");
            try { Directory.CreateDirectory(Path.GetDirectoryName(reportPath)); File.WriteAllText(reportPath, sb.ToString()); } catch (System.Exception ex) { Debug.LogWarning("[OzLoudness] " + ex.Message); }
            Debug.Log($"[OzLoudness] probe {game.level}: {pass} PASS, {warn} WARN, {fail} FAIL -> {reportPath}");
        }

        void Fire(string id, Vector3 at, RtsCamera rts, ZGame game, AudioDirector ad, CombatAudio ca, ref string loopKey)
        {
            switch (id)
            {
                case "alan": AudioDirector.Say("vo_order_ack"); break;
                case "rachel": AudioDirector.Announce("ann_robot_manufactured"); break;
                case "ui_click": case "ui_hover": case "ui_select_unit": case "ui_error": AudioDirector.UI(id); break;
                case "bolt": ca.PlayPitched("bolt", at, LegacyMatch.GainBolt, 0f, -1, 1f, LegacyMatch.BankBolt); break;
                case "heavy_tank_shot": ca.PlayPitched("tankshot", at, LegacyMatch.GainHeavyTankShot, 0f, -1, 1f, LegacyMatch.BankHeavyTankShot); break;
                case "explosion": ca.PlayPitched("explosion", at, LegacyMatch.GainExplosion, 0f, -1, 1f, LegacyMatch.BankExplosion); break;
                case "vehicle_death": ca.PlayPitched("vdeath", at, LegacyMatch.GainHeavy, 0f, -1, 1f, LegacyMatch.BankHeavy); break;
                case "flag_capture": ca.PlayPitched("ter_flag_capture", at, 0.8f, 0f, -1, 1f, "flag-capture-1"); break;
                case "bld_door_open": case "ter_flag_raise": case "rob_step_sand": AudioDirector.Fire(id, at, -7777); break;
                case "rob_squad_march": case "bld_factory_hum": case "veh_tracks_loop":
                    loopKey = "probe:" + id; ad.StartLoop(loopKey, id, at); break;
                case "water":
                    loopKey = "probe:water"; ad.StartLoop(loopKey, game.level == "L17" ? "amb_l17_harbour" : "amb_l02_river", at); break;
                case "offscreen":
                    {
                        var cam = Camera.main; var right = cam ? Vector3.ProjectOnPlane(cam.transform.right, Vector3.up).normalized : Vector3.right;
                        float halfW = rts.height / Mathf.Sin(rts.pitch * Mathf.Deg2Rad) * Mathf.Tan(16f * Mathf.Deg2Rad) * (cam ? cam.aspect : 1.6f);   // check: the real aspect (was 16:9)
                        var p = at + right * halfW * 3f; p.y = game.GroundY(p.x, p.z) + 1f;   // viewport x 2.0 = one screen beyond the right edge (distant rule -11.5 dB)
                        ca.PlayPitched("explosion", p, LegacyMatch.GainExplosion, 0f, -1, 1f, LegacyMatch.BankExplosion);
                        break;
                    }
            }
        }

        /// <summary></summary>
        static string QcDir()
        {
            var d = new DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 6 && d != null; i++, d = d.Parent)
                if (Directory.Exists(Path.Combine(d.FullName, "work")) && Directory.Exists(Path.Combine(d.FullName, "projects")))
                    return Path.Combine(d.FullName, "work", "records", "qc");
            return Application.persistentDataPath;
        }
    }
}
