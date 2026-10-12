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
    /// lights_tail_v1 Play proof (editor only): ONE auto-started L02 run at night in the GPU-light test mode covering both queue rows
    /// (PlayerPrefs oz_ltail_auto = "L02_lights", oz_testmode 1; the key is cleared at start). CPU bots cleared, clock frozen at night.
    ///    lamps face the camera): their HeadlightVariety looks logged, frame B1; one Flicker-plan and one Dead-plan tank set to 35 % hp
    ///    (below 40 % EVERY vehicle has its fault now): 8 s of lamp samples (FaultK, lamp share, spot / pool shift, bursts, the
    ///    ngt_lamp_flicker crackles: each VL BURST line followed in the same frame by its AD PLAY line; no whole-vehicle flicker any
    ///    more), frame B2; the flickering one set to 15 %: the lamp dies with ngt_lamp_spark + ngt_lamp_glass, frame B3.
    ///    unkillable) attacks our fort (first: all 4 beacons per fort seated on the live fort surface within 0.05 m of the 7.045 m
    ///    deck): beacons ON at the ALARM START line, frames A1 night + A2 day, still on 20 s later (the klaxon
    ///    burst is long over), then everybody leaves (stragglers removed after 6 s): the beacons spin down and are OFF within ~3 s of
    ///    the danger leaving FORT with the bld_radar_dish_stop spin-down whine (a relight within the attack: ngt_searchlight_on), and
    ///    stay off.
    /// 86 C held 30 s or 90 C at once (GpuGuard); watchdog 240 s without progress (one ~100 s L02 start hitch in the editor).
    /// </summary>
    public class LightsTailProof : MonoBehaviour
    {
        static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../.."));
        string job, dir; float lastMark; bool done; int errors; float hot = -1f, lastSample = -1f; int peak = -1;
        readonly StringBuilder log = new StringBuilder();
        readonly List<string> verdicts = new List<string>();
        readonly List<OzSoundLog.Entry> sounds = new List<OzSoundLog.Entry>(); float soundSeen = -1f; int seenAtLast;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Auto()
        {
            string job = PlayerPrefs.GetString("oz_ltail_auto", "");
            if (string.IsNullOrEmpty(job)) return;
            PlayerPrefs.DeleteKey("oz_ltail_auto"); PlayerPrefs.Save();
            var go = new GameObject("LightsTailProof"); DontDestroyOnLoad(go);
            go.AddComponent<LightsTailProof>().job = job;
        }

        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        float T => (float)clock.Elapsed.TotalSeconds;
        void Mark(string s) { lastMark = T; log.AppendLine($"[{T,7:0.00}] {s}"); Debug.Log("[ltail] " + s); }
        void Verdict(string step, bool ok, string what) { string v = $"{step}: {(ok ? "PASS" : "FAIL")} - {what}"; verdicts.Add(v); Mark("VERDICT " + v); }
        IEnumerator Wait(float s) { float end = T + s; while (T < end && !done) { lastMark = T; Poll(); yield return null; } }
        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; if (!done) Finish("disabled"); }
        void OnLog(string c, string st, LogType type)
        {
            if (c.StartsWith("[ltail]")) return;
            if (type == LogType.Error || type == LogType.Exception) { errors++; log.AppendLine($"[{T,7:0.00}] ERROR {c.Split('\n')[0]}"); }
            else if (c.StartsWith("[OpenZ] FortAlarmBeacons")) log.AppendLine($"[{T,7:0.00}]   console: {c.Split('\n')[0]}");
        }

        void Poll()
        {   // the OzSoundLog ring holds 2000 entries: keep our own copy, in order. review fix: lines added later in a frame already
            // polled (same unscaled t, e.g. from a LateUpdate) are kept too (was: e.t > soundSeen dropped them)
            int skip = soundSeen < 0f ? 0 : seenAtLast;
            foreach (var e in OzSoundLog.Since(soundSeen < 0f ? 0f : soundSeen))
            {
                if (e.t < soundSeen) continue;
                if (e.t == soundSeen && skip > 0) { skip--; continue; }
                sounds.Add(e);
            }
            if (sounds.Count > 0)
            {
                soundSeen = sounds[sounds.Count - 1].t; seenAtLast = 0;
                for (int i = sounds.Count - 1; i >= 0 && sounds[i].t == soundSeen; i--) seenAtLast++;
            }
        }
        /// <summary>review fix: lines of src / key / outcome prefix since t0; paired = followed by an AD PLAY of playKey (the one-shot
        /// the line announced) within maxGap s, or, with maxGap &lt; 0, as the very next log line in the same frame.</summary>
        void Paired(string src, string key, string prefix, string playKey, float since, float maxGap, out int lines, out int paired)
        {
            Poll(); lines = 0; paired = 0;
            for (int i = 0; i < sounds.Count; i++)
            {
                var e = sounds[i];
                if (e.t < since || e.src != src || e.key != key || e.outcome == null || !e.outcome.StartsWith(prefix)) continue;
                lines++;
                for (int j = i + 1; j < sounds.Count && (maxGap < 0f ? j == i + 1 && sounds[j].t == e.t : sounds[j].t - e.t <= maxGap); j++)
                {
                    var n = sounds[j];
                    if (n.src == "AD" && n.key == playKey && n.outcome != null && n.outcome.StartsWith("PLAY")) { paired++; break; }
                }
            }
        }
        int Played(string key, float since) { Poll(); int n = 0; foreach (var e in sounds) if (e.t >= since && e.key == key && e.outcome != null && e.outcome.StartsWith("PLAY")) n++; return n; }
        float FirstLine(string key, string prefix, float since) { Poll(); foreach (var e in sounds) if (e.t >= since && e.key == key && e.outcome != null && e.outcome.StartsWith(prefix)) return e.t; return -1f; }

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
                if (T - lastMark > 240f) { Finish("watchdog: no progress for 240 s"); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        ZGame g; ZWorld W; RtsCamera cam; DayNight dn; float h0;
        IEnumerator SkipBriefing()
        {
            float w0 = T; bool aborted = false; var pf = typeof(ZodBriefing).GetField("perf", BindingFlags.NonPublic | BindingFlags.Instance);
            double sim0 = W.Time;
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
            Mark($"briefing over after {T - w0:0.0} s");
        }

        // ---------------------------------------------------------------- helpers (BundleFortProof's)
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
        void SetFrac(ZObject o, float f) { o.SetHealth(Mathf.Max(1, (int)(o.MaxHealth * (double)f))); W.UpdateObjectHealth(o); }
        void Focus(Vector3 at, float h) { cam.focus = new Vector3(at.x, 0f, at.z) + Flat(cam.transform.forward).normalized * (at.y * 0.9f); cam.SetHeightImmediate(h); }
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
        IEnumerator TimeOfDay(float tod) { dn.timeOfDay = tod; dn.Apply(tod); yield return Wait(1.2f); }
        IEnumerator Snap(string file, string what)
        {
            yield return null; yield return new WaitForEndOfFrame();
            var tb = ScreenCapture.CaptureScreenshotAsTexture(); File.WriteAllBytes(Path.Combine(dir, file), tb.EncodeToPNG());
            Mark($"frame {file} {tb.width}x{tb.height}; {what}"); Destroy(tb);
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
        static string P(VehicleLights.LampProbe p) =>
            $"{p.Type} fault {p.Plan.mode}/{(p.Plan.side < 0 ? "L" : "R")} at {p.Plan.at:P0}: FaultK {p.FaultK:F2}, dead {p.Dead}, share {p.Share:F2}, spot {p.SpotIntensity:F1} (range {p.SpotRange:F1} m), " +
            $"spot shift {p.SpotShiftM:F2} m, pool shift {p.PoolShiftM:F2} m, glass {p.FaultGlass:F2} / {p.KeepGlass:F2} (fault / other, {p.FaultGlassN} fault-side), bursts {p.Bursts}, crackles {p.FlickSounds}; look {p.Look}";
        /// <summary>review fix 3: the model's headlamp glass on the fault side is dark (<= 0.15 x the other lamp's glass, which burns).</summary>
        static bool GlassOut(VehicleLights.LampProbe p) => p.FaultGlassN >= 1 && p.KeepGlass > 1f && p.FaultGlass <= 0.15f * p.KeepGlass;

        // ---------------------------------------------------------------- the run
        IEnumerator Start()
        {
            lastMark = T; float w0 = T;
            while ((!ZGame.I || ZGame.I.World == null) && T - w0 < 90f) yield return null;
            if (!ZGame.I || ZGame.I.World == null) { Finish("the level never came up within 90 s"); yield break; }
            g = ZGame.I; W = g.World; cam = FindFirstObjectByType<RtsCamera>(); dn = FindFirstObjectByType<DayNight>();
            dir = Path.Combine(Root, "work/records/milestones/lights_tail_v1", job); Directory.CreateDirectory(dir);
            StartCoroutine(Heat());
            h0 = cam.height;
            Mark($"lights_tail_v1 proof {job} {DateTime.Now:yyyy-MM-dd HH:mm:ss}, scene {SceneManager.GetActiveScene().name}, oz_testmode {PlayerPrefs.GetInt("oz_testmode", 0)}, screen {Screen.width}x{Screen.height}, level {g.level}, player {g.player}, RTS height {h0:0.0} m");
            int bots = W.Bots.Count; W.Bots.Clear();
            yield return SkipBriefing();
            if (done) yield break;
            Mark($"CPU bots cleared ({bots})");
            Application.targetFrameRate = 30;                                                     // logic + a few frames (heat)
            bool run0 = dn && dn.running; if (dn) dn.running = false;
            if (dn) yield return TimeOfDay(0.62f);                                               // night
            var fort = W.Objects.Find(o => o.IsFort && o.Owner == g.player && !o.Destroyed);
            if (fort == null || !VehicleLights.I || !FortAlarmBeacons.I) { Verdict("bind", false, $"fort {(fort != null)}, VehicleLights {(bool)VehicleLights.I}, FortAlarmBeacons {(bool)FortAlarmBeacons.I}"); Finish(null); yield break; }
            Unkillable(fort); W.UpdateObjectHealth(fort);
            Mark("FortAlarmBeacons: " + FortAlarmBeacons.I.Status() + "\n  seats: " + FortAlarmBeacons.I.Seats);
            yield return Headlights(fort);
            if (!done) yield return Alarm(fort);
            if (dn) dn.running = run0;
            Finish(null);
        }

        IEnumerator Headlights(ZObject fort)
        {
            var vl = VehicleLights.I;
            int mw = W.Map.WidthPix, mh = W.Map.HeightPix;
            double mx = mw * 0.5 - fort.CenterX, my = mh * 0.5 - fort.CenterY, ml = Math.Sqrt(mx * mx + my * my); mx /= ml; my /= ml;
            // the row: 300 px beside the fort, across the axis to the map centre (off the siege path), whichever side stays on the map
            int cx = 0, cy = 0; double best = -1;
            foreach (int side in new[] { 1, -1 })
            {
                int x = (int)(fort.CenterX + mx * 60 - my * 300 * side), y = (int)(fort.CenterY + my * 60 + mx * 300 * side);
                double margin = Math.Min(Math.Min(x, mw - x), Math.Min(y, mh - y));
                if (margin > best) { best = margin; cx = x; cy = y; }
            }
            cx = Mathf.Clamp(cx, 160, mw - 160); cy = Mathf.Clamp(cy, 120, mh - 160);
            var row = new List<ZObject>();
            for (int i = 0; i < 6; i++) { var o = Machine("vehicle", "light", g.player, cx + (int)((i - 2.5f) * 44f), cy); if (o != null) row.Add(o); }
            foreach (var o in row) MoveTo(o, o.CenterX, o.CenterY + 48);                          // 3 tiles south: the lamps face the camera
            Mark($"row: {row.Count} crewed {g.player} light tanks around px ({cx}, {cy}), unkillable, driving 3 tiles south");
            yield return Wait(6.5f);
            var rowC = ZGame.PxToWorld(cx, cy + 48);
            Focus(rowC, 26f); yield return Wait(1.5f);
            var looks = new HashSet<string>(); float pMin = 9f, pMax = 0f, sMin = 9f, sMax = 0f; bool inBand = true;
            foreach (var o in row)
            {
                var p = vl.Probe(o.Id); if (!p.Ok) { Mark($"  #{o.Id}: NO RIG"); continue; }
                looks.Add(p.Look.Key + "r" + p.Look.reachStep); pMin = Mathf.Min(pMin, p.Look.power); pMax = Mathf.Max(pMax, p.Look.power);
                sMin = Mathf.Min(sMin, p.Look.size); sMax = Mathf.Max(sMax, p.Look.size);
                inBand &= Mathf.Abs(p.Look.size - 1f) <= 0.071f && Mathf.Abs(p.Look.power - 1f) <= 0.111f && Mathf.Abs(p.Look.reach - 1f) <= 0.111f;
                Mark($"  #{o.Id} {P(p)}");
            }
            yield return Snap("B1_variety_row_night.png", "6 crewed light tanks at night, 26 m: each with its own subtle lamp look");
            Verdict("variety", looks.Count >= 3 && pMax - pMin >= 0.05f && inBand,
                    $"{looks.Count} different look steps in a row of {row.Count}; power {pMin:F3}..{pMax:F3}, glow size {sMin:F3}..{sMax:F3}, all within +-7 % size / +-11 % power / reach");

            // faults: one Flicker-plan and one Dead-plan tank to 35 % hp
            ZObject fl = null, dd = null;
            for (int extra = 0; extra < 8 && (fl == null || dd == null); extra++)
            {
                foreach (var o in row)
                {
                    var plan = HeadlightFaults.For(o.Id);
                    if (fl == null && plan.mode == HeadlightFaults.Mode.Flicker) fl = o;
                    else if (dd == null && plan.mode == HeadlightFaults.Mode.Dead && o != fl) dd = o;
                }
                if (fl == null || dd == null) { var o = Machine("vehicle", "light", g.player, cx + (int)((row.Count - 2.5f) * 44f), cy + 48); if (o != null) row.Add(o); yield return Wait(0.6f); }
            }
            if (fl == null || dd == null) { Verdict("faults_bind", false, "no Flicker / Dead plan in the row"); yield break; }
            float t0 = Time.unscaledTime;
            SetFrac(fl, 0.35f); SetFrac(dd, 0.35f);
            Mark($"#{fl.Id} (Flicker plan) and #{dd.Id} (Dead plan) set to 35 % hp (below 40 %: every vehicle shows its fault)");
            float kMin = 9f, kMax = 0f, powMin = 9f, gMin = 9f, gMax = -1f; int powN = 0; bool snapped = false; float sampleEnd = T + 8f;
            while (T < sampleEnd && !done)
            {
                var pf = vl.Probe(fl.Id); kMin = Mathf.Min(kMin, pf.FaultK); kMax = Mathf.Max(kMax, pf.FaultK);
                var pd = vl.Probe(dd.Id); powMin = Mathf.Min(powMin, Mathf.Min(pf.Power, pd.Power)); powN++;   // review fix: whole-vehicle power (the old damage flicker)
                if (pf.FaultGlassN >= 1 && pf.KeepGlass > 1f) { float gr = pf.FaultGlass / pf.KeepGlass; gMin = Mathf.Min(gMin, gr); gMax = Mathf.Max(gMax, gr); }   // review fix 3: the glass flickers too
                if (!snapped && T > sampleEnd - 6f && pf.FaultK < 0.3f) { snapped = true; yield return Snap("B2_faults_night.png", $"#{dd.Id} one lamp OUT, #{fl.Id} one lamp flickering (FaultK {pf.FaultK:F2} at the capture)"); }
                lastMark = T; Poll(); yield return null;
            }
            if (!snapped) yield return Snap("B2_faults_night.png", $"#{dd.Id} one lamp OUT, #{fl.Id} one lamp flickering (no burst low caught: FaultK {vl.Probe(fl.Id).FaultK:F2})");
            var pD = vl.Probe(dd.Id); var pF = vl.Probe(fl.Id);
            Mark($"  dead  #{dd.Id} {P(pD)}"); Mark($"  flick #{fl.Id} {P(pF)} (8 s: FaultK {kMin:F2}..{kMax:F2})");
            int crackles = Played("ngt_lamp_flicker", t0), sparks0 = Played("ngt_lamp_spark", t0), glass0 = Played("ngt_lamp_glass", t0);
            Paired("VL", "ngt_lamp_flicker", "BURST #" + fl.Id + " ", "ngt_lamp_flicker", t0, -1f, out int burstLines, out int burstPlays);   // review fix: only the crackles of THIS lamp's bursts
            Verdict("lamp_out", pD.Dead && pD.FaultK <= 0.001f && Mathf.Abs(pD.Share - 0.5f) < 0.01f && pD.PoolShiftM > 0.2f && pD.SpotShiftM > 0.2f && GlassOut(pD) && sparks0 >= 1 && glass0 >= 1,
                    $"#{dd.Id} at 35 %: glow + cone off (FaultK {pD.FaultK:F2}), headlamp glass dark ({pD.FaultGlass:F2} <= 0.15 x {pD.KeepGlass:F2}, {pD.FaultGlassN} fault-side), real spot / pool at half ({pD.Share:F2}) and shifted to the other lamp (spot {pD.SpotShiftM:F2} m, pool {pD.PoolShiftM:F2} m); ngt_lamp_spark {sparks0} + ngt_lamp_glass {glass0} PLAY");
            float kTop = 0.75f * pF.Look.weak;                                                            // review fix: FaultK = level x the weaker lamp (between bursts 0.8 x weak)
            Verdict("lamp_flicker", !pF.Dead && kMin < 0.2f && kMax >= kTop && pF.Bursts >= 1 && gMin <= 0.4f && gMax >= 0.5f,
                    $"#{fl.Id} at 35 %: FaultK {kMin:F2}..{kMax:F2} over 8 s (top >= {kTop:F2} = 0.75 x weak {pF.Look.weak:F2}), {pF.Bursts} bursts (every 2-5 s); headlamp glass {gMin:F2}..{gMax:F2} of the other lamp's; glow, cone, glass, spot and pool all use FaultK");
            Verdict("flicker_sound", pF.FlickSounds >= 1 && burstLines >= 1 && burstPlays >= 1 && burstPlays == burstLines,
                    $"#{fl.Id}: {pF.FlickSounds} crackles requested by VehicleLights.FlickerSound, {burstLines} 'VL ngt_lamp_flicker BURST #{fl.Id}' lines, {burstPlays} of them followed by their 'AD ngt_lamp_flicker PLAY' in the same frame " +
                    $"({crackles} ngt_lamp_flicker PLAY lines in all, street / fort lamps included) for {pF.Bursts} bursts");
            Verdict("no_whole_flicker", powN > 0 && powMin >= 0.98f,
                    $"#{fl.Id} + #{dd.Id} at 35 % (ShowDamaged): whole-vehicle lamp power min {powMin:F2} over {powN} samples (the old silent damage flicker of both lamps, tails and beacons is retired; only the one fault lamp flickers, with its crackle)");
            float t1 = Time.unscaledTime;
            SetFrac(fl, 0.15f);
            Mark($"#{fl.Id} set to 15 % hp: the flickering lamp must die now");
            yield return Wait(1.5f);
            pF = vl.Probe(fl.Id);
            int sp = Played("ngt_lamp_spark", t1), gl = Played("ngt_lamp_glass", t1);
            Mark($"  flick #{fl.Id} {P(pF)}");
            yield return Snap("B3_flicker_died_night.png", $"#{fl.Id} at 15 %: its flickering lamp died (spark + glass), #{dd.Id} one lamp out");
            Verdict("flicker_dies", pF.Dead && pF.FaultK <= 0.001f && GlassOut(pF) && sp >= 1 && gl >= 1,
                    $"#{fl.Id} below 20 %: lamp dead {pF.Dead}, headlamp glass dark ({pF.FaultGlass:F2} <= 0.15 x {pF.KeepGlass:F2}), ngt_lamp_spark {sp} + ngt_lamp_glass {gl} PLAY");
            Mark(vl.Status());
        }

        IEnumerator Alarm(ZObject red)
        {
            var fab = FortAlarmBeacons.I; var ad = AudioDirector.I;
            var fv = g.StaticViews.TryGetValue(red.Id, out var fgo) ? fgo : null;
            var fortPos = fv ? fv.transform.position : ZGame.PxToWorld(red.CenterX, red.CenterY);
            Focus(fortPos, Mathf.Min(h0, 36f)); yield return Wait(0.8f);
            Team enemy = g.player == Team.Red ? Team.Blue : Team.Red;
            double mx = W.Map.WidthPix * 0.5 - red.CenterX, my = W.Map.HeightPix * 0.5 - red.CenterY, ml = Math.Sqrt(mx * mx + my * my); mx /= ml; my /= ml;
            Func<double, double, (int, int)> At = (d, side) => ((int)(red.CenterX + mx * d - my * side), (int)(red.CenterY + my * d + mx * side));
            int starts0 = AudioDirector.AlarmStarts; float log0 = Time.unscaledTime;
            bool onlyOurs = fab.FortIds.Count > 0; foreach (int id in fab.FortIds) { var f = W.Get(id); onlyOurs &= f != null && f.IsFort && f.Owner == g.player; }
            Verdict("seated", Seated(fab, out string seatWhy), seatWhy);                                   // review fix: on the fort, not floating / clipping
            Verdict("our_fort_only", onlyOurs && fab.Count == 4 * fab.FortIds.Count, $"{fab.Count} beacons on fort(s) {string.Join(",", fab.FortIds)} (owner {g.player}); state before the attack {fab.State}");
            var wave = new List<ZObject>();
            Action<ZObject> Add = o => { if (o == null) return; wave.Add(o); if (o.IsRobot) wave.AddRange(o.Minions); };
            { var p = At(170, -40); Add(Machine("vehicle", "light", enemy, p.Item1, p.Item2)); p = At(175, 40); Add(Machine("vehicle", "light", enemy, p.Item1, p.Item2)); p = At(150, 0); Add(Squad("grunt", enemy, p.Item1, p.Item2)); }
            foreach (var o in wave) if (!o.IsMinion) AttackFort(o, red);
            Mark($"wave A (L02_siege): {wave.Count} {enemy} units (unkillable) attack our fort #{red.Id}");
            float w0 = T; float tAlarm = -1f;
            while (T - w0 < 40f && !done) { if (AudioDirector.AlarmStarts > starts0) { tAlarm = Time.time; break; } lastMark = T; Poll(); yield return null; }
            if (tAlarm < 0f) { Verdict("alarm_start", false, $"no klaxon within 40 s (enemies near {NearFort(red, enemy)}, danger FORT {AudioDirector.FortDanger})"); yield break; }
            float wOn = T; while (fab.State != FortAlarmBeacons.Phase.On && T - wOn < 1f) { yield return null; }
            float alarmLine = FirstLine("bld_fort_alarm", "ALARM START", log0), onLine = FirstLine("bld_fort_alarm", "BEACONS ON", log0);
            Verdict("alarm_sync", fab.State == FortAlarmBeacons.Phase.On && fab.OnAt >= 0f && Mathf.Abs(fab.OnAt - tAlarm) <= 0.1f && alarmLine >= 0f && onLine >= 0f && Mathf.Abs(onLine - alarmLine) <= 0.1f,
                    $"klaxon ALARM START at {alarmLine - log0:F2} s, BEACONS ON at {onLine - log0:F2} s (OzSoundLog); beacons on {fab.OnAt - tAlarm:+0.00;-0.00} s after the klaxon was seen");
            yield return Wait(3f);
            yield return Snap("A1_beacons_night.png", $"our fort under attack at night: {fab.Count} beacons spinning ({fab.RealLit} real red spots granted by the night budget)");
            if (dn) { yield return TimeOfDay(0.08f); yield return Snap("A2_beacons_day.png", "the same alarm by day (dome flash + halo)"); yield return TimeOfDay(0.62f); }
            while (Time.time - tAlarm < 20f && !done) { lastMark = T; Poll(); yield return null; }
            bool klaxonOff = !(ad && ad.LoopActive("fortalarm"));
            Verdict("on_during_siege", fab.State == FortAlarmBeacons.Phase.On && AudioDirector.FortDanger && klaxonOff,
                    $"+{Time.time - tAlarm:F0} s: beacons {fab.State} (level {fab.Level:F2}, spin {fab.Spin:F2}), danger FORT {AudioDirector.FortDanger}, klaxon {(klaxonOff ? "already over (one burst per attack)" : "still on")}");
            foreach (var o in wave) if (!o.IsMinion) { var p = At(460, (o.Id % 3 - 1) * 40); MoveTo(o, p.Item1, p.Item2); }
            Mark("everybody leaves");
            yield return Wait(6f);
            int k = 0; foreach (var o in wave) if (!o.Destroyed) { Kill(o); k++; }
            Mark($"{k} wave units removed (a clean end of the attack); enemies within 250 px: {NearFort(red, enemy)}");
            float wd = T; float tDanger = -1f;
            while (T - wd < 30f && !done) { if (!AudioDirector.FortDanger) { tDanger = Time.time; break; } lastMark = T; Poll(); yield return null; }
            if (tDanger < 0f) { Verdict("fade_off", false, "the danger never left FORT within 30 s"); yield break; }
            var spins = new List<float>(); var levels = new List<float>(); float wf = T;
            while (fab.State != FortAlarmBeacons.Phase.Off && T - wf < 8f && !done) { spins.Add(fab.Spin); levels.Add(fab.Level); lastMark = T; Poll(); yield return null; }
            bool mono = true; for (int i = 1; i < spins.Count; i++) mono &= spins[i] <= spins[i - 1] + 1e-4f && levels[i] <= levels[i - 1] + 1e-4f;
            float offDelay = fab.OffAt - tDanger, fade = fab.OffAt - fab.SpinDownAt;
            Verdict("fade_off", fab.State == FortAlarmBeacons.Phase.Off && offDelay >= 0f && offDelay <= 3.1f && fade >= 1.6f && fade <= 2.6f && mono,
                    $"danger left FORT at +{tDanger - tAlarm:F1} s; beacons OFF {offDelay:F2} s later after a {fade:F2} s spin-down (spin {(spins.Count > 0 ? spins[0] : 0f):F2} -> 0 and level falling every frame: {mono}, {spins.Count} samples)");
            Paired("FAB", "bld_fort_alarm", "BEACONS SPIN DOWN", fab.spinDownSound, log0, 0.1f, out int downs, out int downSnd);
            Paired("FAB", "bld_fort_alarm", "BEACONS ON " + fab.Count + " again", fab.relightSound, log0, 0.1f, out int re1, out int re1Snd);
            Paired("FAB", "bld_fort_alarm", "BEACONS ON " + fab.Count + " back on", fab.relightSound, log0, 0.1f, out int re2, out int re2Snd);
            Verdict("beacon_sounds", downs >= 1 && downSnd == downs && re1Snd + re2Snd == re1 + re2,
                    $"{downs} BEACONS SPIN DOWN lines, {downSnd} with their 'AD {fab.spinDownSound} PLAY' within 0.1 s; {re1 + re2} relights without a klaxon, {re1Snd + re2Snd} with their 'AD {fab.relightSound} PLAY' (every visible beacon change has a sound)");
            yield return Wait(4f);
            Verdict("never_stuck", fab.State == FortAlarmBeacons.Phase.Off && fab.Level <= 0f && fab.RealLit == 0, $"4 s later: {fab.State}, level {fab.Level:F2}, real lights {fab.RealLit}");
            Mark(fab.Status());
            var lines = new StringBuilder();
            foreach (var e in sounds) if (e.t >= log0 && (e.key == "bld_fort_alarm" || e.key == fab.spinDownSound || e.key == fab.relightSound)) lines.AppendLine($"  {e.t - log0,7:0.00} s  {e.src,-4} {e.key,-20} {e.outcome}");
            Mark("klaxon + beacon lines in OzSoundLog:\n" + lines);
        }

        /// <summary>lights_tail_v1 review fix: every beacon of every one of our forts was seated on the LIVE fort surface
        /// (TowerMounts.FortSurface, "(surface, measured 7.045)"), none fell back to the measured height ("(measured)") or had its
        /// surface rejected, and each seat is within 0.05 m of the measured 7.045 m deck (the flat strip there is only ~0.5 m wide:
        /// a frame / scale mismatch would land off it).</summary>
        static bool Seated(FortAlarmBeacons fab, out string why)
        {
            const float Deck = 7.045f, Tol = 0.05f;
            string seats = fab.Seats ?? "";
            var forts = seats.Split(new[] { "; " }, StringSplitOptions.RemoveEmptyEntries);
            var rx = new System.Text.RegularExpressions.Regex(@"y (\d+[.,]\d+) \(surface");
            int surf = 0, off = 0; bool perFort = forts.Length == fab.FortIds.Count && forts.Length > 0; float yMin = float.MaxValue, yMax = float.MinValue;
            foreach (var f in forts)
            {
                var ms = rx.Matches(f);
                perFort &= ms.Count == 4;
                foreach (System.Text.RegularExpressions.Match m in ms)
                {
                    float y = float.Parse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
                    surf++; yMin = Mathf.Min(yMin, y); yMax = Mathf.Max(yMax, y);
                    if (Mathf.Abs(y - Deck) > Tol) off++;
                }
            }
            bool rejected = seats.Contains("rejected");
            bool ok = perFort && !rejected && off == 0 && surf == 4 * fab.FortIds.Count;
            why = $"{surf} '(surface' seats on {forts.Length} fort(s) (4 each: {perFort}), rejected {rejected}, " +
                  (surf > 0 ? $"y {yMin:F3}..{yMax:F3}, {off} off the {Deck:F3} m deck by > {Tol:F2} m" : "no surface seat") + $" ({seats})";
            return ok;
        }

        void Finish(string why)
        {
            if (done) return; done = true;
            try
            {
                Application.targetFrameRate = 60;
                foreach (var o in staged) Kill(o);
                Mark(why == null ? "DONE" : "STOPPED: " + why);
                log.AppendLine($"errors/exceptions in the Console during the run: {errors}; GPU peak (GpuGuard) {peak} C");
                log.AppendLine("## VERDICTS"); foreach (var v in verdicts) log.AppendLine("  " + v);
                if (dir != null)
                {
                    Poll();
                    File.WriteAllText(Path.Combine(dir, "ozsoundlog.txt"), OzSoundLog.Format(sounds));
                    File.WriteAllText(Path.Combine(dir, "proof_log.txt"), log.ToString());
                }
            }
            catch (Exception e) { Debug.LogError("[ltail] finish: " + e); }
            UnityEditor.EditorApplication.isPlaying = false;
        }
    }
}
#endif
