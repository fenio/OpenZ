#if !OZ_RELEASE
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace OpenZ
{
    /// <summary></summary>
    public sealed partial class OzSoak
    {
        // ------------------------------------------------------------------ pass / fail bars (README "bars")
        public const float LeakPct = 5f;                    // menu checkpoint growth vs cycle 1, memory and counts
        static readonly Dictionary<string, int> LeakObjectsPerCycle = new Dictionary<string, int>
        {
            ["gameobjects"] = 50, ["components"] = 200, ["textures"] = 5, ["rendertextures"] = 2, ["meshes"] = 20, ["materials"] = 20,
            ["audioclips"] = 5, ["audiosources"] = 10, ["particlesystems"] = 10, ["inactive_gameobjects"] = 50, ["registry_items"] = 200, ["pool_items"] = 100,
        };
        public const float LeakMinMb = 4f;                  // memory: > 5 % AND > 4 MB (a 5 % wobble of a 30 MB heap is no leak)
        public const float SlopeWarnMb = 3f, SlopeFailMb = 10f;   // MB / min, total allocated, in a match after its first 45 s
        public const float GcSpikeMs = 2f, HitchMs = 50f, HitchFailMs = 100f;

        // ------------------------------------------------------------------ census
        struct Census { public int go, goActive, inactive, comp, tex, tex2d, rt, cube, mesh, mat, clip, asrc, asrcPlaying, ps, psPlaying, poolItems, regItems; }
        Census last;
        Dictionary<string, int> lastTypes, lastNames;
        readonly Dictionary<string, int> lastRegs = new Dictionary<string, int>(), matchRegs = new Dictionary<string, int>();

        Census TakeCensus(bool full)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var c = new Census();
            foreach (var g in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (!g.scene.IsValid()) continue;                                                        // prefab / asset objects
                c.go++; if (g.activeInHierarchy) c.goActive++; else c.inactive++;
            }
            foreach (var k in Resources.FindObjectsOfTypeAll<Component>()) if (k && k.gameObject.scene.IsValid()) c.comp++;
            foreach (var t in Resources.FindObjectsOfTypeAll<Texture>()) { c.tex++; if (t is RenderTexture) c.rt++; else if (t is Texture2D) c.tex2d++; else if (t is Cubemap) c.cube++; }
            c.mesh = Resources.FindObjectsOfTypeAll<Mesh>().Length;
            c.mat = Resources.FindObjectsOfTypeAll<Material>().Length;
            c.clip = Resources.FindObjectsOfTypeAll<AudioClip>().Length;
            foreach (var a in Resources.FindObjectsOfTypeAll<AudioSource>()) if (a && a.gameObject.scene.IsValid()) { c.asrc++; if (a.isPlaying) c.asrcPlaying++; }
            foreach (var p in Resources.FindObjectsOfTypeAll<ParticleSystem>()) if (p && p.gameObject.scene.IsValid()) { c.ps++; if (p.isPlaying) c.psPlaying++; }
            ScanRegistries(out c.regItems, out c.poolItems);
            if (full)
            {   // checkpoints: every loaded object by type, and the asset names that repeat (instance leaks show as many "X (Instance)")
                lastTypes = new Dictionary<string, int>(); lastNames = new Dictionary<string, int>();
                foreach (var o in Resources.FindObjectsOfTypeAll<Object>())
                {
                    if (!o) continue;
                    var tn = o.GetType().Name; lastTypes[tn] = lastTypes.TryGetValue(tn, out var v) ? v + 1 : 1;
                    if (o is Material || o is Texture || o is Mesh || o is AudioClip)
                    {
                        string nm = tn + ": " + (string.IsNullOrEmpty(o.name) ? "(no name)" : o.name);
                        lastNames[nm] = lastNames.TryGetValue(nm, out var w) ? w + 1 : 1;
                    }
                }
            }
            last = c;
            float ms = (float)sw.Elapsed.TotalMilliseconds;
            censusCsv?.WriteLine(F(Time.unscaledTime - runStart) + "," + cycle + "," + phase + "," + Csv(SceneManager.GetActiveScene().name) + "," + F(levelStart > 0f ? Time.unscaledTime - levelStart : -1f) + "," +
                                 c.go + "," + c.goActive + "," + c.inactive + "," + c.comp + "," + c.tex + "," + c.tex2d + "," + c.rt + "," + c.cube + "," + c.mesh + "," + c.mat + "," + c.clip + "," +
                                 c.asrc + "," + c.asrcPlaying + "," + c.ps + "," + c.psPlaying + "," + c.poolItems + "," + c.regItems + "," + F(ms));
            return c;
        }

        // ------------------------------------------------------------------ static registries + singleton collections (reflection, cached)
        sealed class Reg { public string name; public FieldInfo f; public PropertyInfo count; public MemberInfo owner; public bool pool; }
        List<Reg> regs;

        void BuildRegs()
        {
            regs = new List<Reg>();
            const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            const BindingFlags N = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            Type[] types;
            try { types = typeof(OzSoak).Assembly.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
            foreach (var t in types)
            {
                if (t == typeof(OzSoak) || t.ContainsGenericParameters || t.Namespace == null || !t.Namespace.StartsWith("OpenZ", StringComparison.Ordinal)) continue;
                try { AddRegs(t, S, N); } catch (Exception) { }                                         // an ambiguous "I" or a type that will not reflect: skipped
            }
            Event("registry scan: " + regs.Count + " static / singleton collections");
        }

        void AddRegs(Type t, BindingFlags S, BindingFlags N)
        {
            {
                foreach (var f in t.GetFields(S))
                {
                    if (f.IsLiteral) continue;
                    var cp = CountOf(f.FieldType); if (cp == null) continue;
                    regs.Add(new Reg { name = Nice(t) + "." + f.Name, f = f, count = cp, pool = IsPool(f.Name) });
                }
                // singletons: a static "I" / "Instance" of the type itself -> its instance collections (pools, live lists)
                MemberInfo owner = (MemberInfo)t.GetProperty("I", S) ?? t.GetField("I", S);
                if (owner == null) owner = (MemberInfo)t.GetProperty("Instance", S) ?? t.GetField("Instance", S);
                if (owner == null || !t.IsAssignableFrom(owner is PropertyInfo pi ? pi.PropertyType : ((FieldInfo)owner).FieldType)) return;
                for (var bt = t; bt != null && bt != typeof(MonoBehaviour) && bt != typeof(object); bt = bt.BaseType)
                    foreach (var f in bt.GetFields(N))
                    {
                        var cp = CountOf(f.FieldType); if (cp == null) continue;
                        regs.Add(new Reg { name = Nice(t) + ".I." + f.Name, f = f, count = cp, owner = owner, pool = IsPool(f.Name) });
                    }
            }
        }

        /// <summary>
        /// not a leak: OzSoundLog.ring (capped at 2000 lines) and ScorchDecals' build scratch lists (cleared before every mark, at most one
        /// 21 x 21 grid). They stay in registries.csv and the growth lists, but not in the registry_items / pool_items sums of the verdict.</summary>
        static bool Bounded(string n) => n == "OzSoundLog.ring" || n == "ScorchDecals.bv" || n == "ScorchDecals.buv" || n == "ScorchDecals.bcol" || n == "ScorchDecals.btri";

        static bool IsPool(string n) { n = n.ToLowerInvariant(); return n.Contains("pool") || n.Contains("free") || n.Contains("spare") || n.Contains("idle"); }
        static string Nice(Type t) => t.DeclaringType != null ? Nice(t.DeclaringType) + "+" + t.Name : t.Name;

        static PropertyInfo CountOf(Type ft)
        {
            if (ft.IsArray || ft == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(ft)) return null;
            var p = ft.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
            return p != null && p.PropertyType == typeof(int) && p.GetIndexParameters().Length == 0 ? p : null;
        }

        void ScanRegistries(out int items, out int poolItems)
        {
            items = poolItems = 0;
            if (regs == null) BuildRegs();
            lastRegs.Clear();
            bool inMatch = phase == "match"; if (inMatch) matchRegs.Clear();
            float t = Time.unscaledTime - runStart; string sc = Csv(SceneManager.GetActiveScene().name);
            foreach (var r in regs)
            {
                int n;
                try
                {
                    object host = null;
                    if (r.owner != null) { host = r.owner is PropertyInfo pi ? pi.GetValue(null) : ((FieldInfo)r.owner).GetValue(null); if (host == null || (host is Object uo && !uo)) continue; }
                    var v = r.f.GetValue(host);
                    if (v == null) continue;
                    n = v is ICollection c ? c.Count : (int)r.count.GetValue(v);
                }
                catch (Exception) { continue; }
                lastRegs[r.name] = n; if (inMatch) matchRegs[r.name] = n;
                if (!Bounded(r.name)) { items += n; if (r.pool) poolItems += n; }
                if (n > 0) regCsv?.WriteLine(F(t) + "," + cycle + "," + phase + "," + sc + "," + r.name + "," + n);
            }
        }

        void WriteCheckpoint(Checkpoint cp)
        {
            try
            {
                var b = new StringBuilder();
                b.AppendLine($"checkpoint c{cp.cycle} {cp.label} at {cp.t - runStart:0} s (main menu, after UnloadUnusedAssets + GC.Collect)");
                foreach (var kv in cp.m) b.AppendLine($"{kv.Key} = {kv.Value:0.###}");
                b.AppendLine("-- object types"); foreach (var kv in cp.types.OrderByDescending(k => k.Value)) b.AppendLine($"{kv.Value,7}  {kv.Key}");
                b.AppendLine("-- material / texture / mesh / clip names seen more than once"); foreach (var kv in cp.names.Where(k => k.Value > 1).OrderByDescending(k => k.Value)) b.AppendLine($"{kv.Value,7}  {kv.Key}");
                b.AppendLine("-- registries (static / singleton collections, Count > 0)"); foreach (var kv in cp.regs.Where(k => k.Value > 0).OrderByDescending(k => k.Value)) b.AppendLine($"{kv.Value,7}  {kv.Key}");
                File.WriteAllText(Path.Combine(dir, $"checkpoint_c{cp.cycle}_{cp.label.Replace(' ', '_')}.txt"), b.ToString());
            }
            catch (Exception e) { Debug.LogWarning("[OpenZ Soak] checkpoint file: " + e.Message); }
        }

        // ================================================================== SOAK_REPORT.md
        static string V(bool pass, bool warn = false) => pass ? (warn ? "WARN" : "PASS") : "FAIL";
        static float Pct(List<float> l, float p) { if (l.Count == 0) return -1f; var c = new List<float>(l); c.Sort(); return c[Mathf.Clamp(Mathf.CeilToInt(p * c.Count) - 1, 0, c.Count - 1)]; }

        /// <summary>Least-squares slope (per minute) of y over t (s).</summary>
        static float Slope(List<(float t, float y)> pts)
        {
            if (pts.Count < 3) return float.NaN;
            double mt = pts.Average(p => p.t), my = pts.Average(p => p.y), num = 0, den = 0;
            foreach (var p in pts) { num += (p.t - mt) * (p.y - my); den += (p.t - mt) * (p.t - mt); }
            return den > 0 ? (float)(num / den * 60.0) : float.NaN;
        }

        void WriteReport(string aborted)
        {
            reportWritten = true;
            var b = new StringBuilder();
            var verdicts = new List<(string what, string v, string detail)>();
            b.AppendLine("# OpenZ soak report");
            b.AppendLine();
            b.AppendLine($"- Run: {DateTime.Now:yyyy-MM-dd HH:mm}, {(Time.unscaledTime - runStart) / 60f:0.0} min of {minutes:0.#} asked, {cycles} cycles, {visits.Count} level visits, game speed x{speed:0.#}" + (aborted != null ? $" - **ABORTED: {aborted}**" : ""));
            b.AppendLine($"- Player: {Application.productName} {Application.version} ({OzVersion.Label}), development {Debug.isDebugBuild}, {SystemInfo.graphicsDeviceName}, {SystemInfo.processorType}, RAM {SystemInfo.systemMemorySize} MB");
            b.AppendLine($"- Screen {Screen.width}x{Screen.height} {Screen.fullScreenMode}, graphics {OzQuality.Name}, fps cap {GameBoot.CapFps}, weather option {(OzWeather.Enabled ? "ON" : "OFF")}, heat-capped seconds {heatSecond}");
            b.AppendLine($"- Log: {errors} errors, {exceptions} exceptions (Player.log). Folder: `{dir}`");
            if (OzQuality.Level != 0 || Screen.height < 1440) b.AppendLine($"- NOTE: the 60 fps bar is for HIGH at 2560x1440; this run was {OzQuality.Name} at {Screen.width}x{Screen.height}.");
            if (GameBoot.TestMode) b.AppendLine("- NOTE: test mode (oz_testmode = 1) is ON in this player: 30 fps cap and atmosphere off, so every second counts as capped and the fps verdict cannot be read.");
            if (!OzWeather.Enabled) b.AppendLine("- NOTE: WEATHER is OFF in this player: the forced night storm did not show. Set Options > WEATHER = ON and run again.");
            b.AppendLine();

            // ---- 1. frame time per visit
            var fpsRows = new StringBuilder();
            fpsRows.AppendLine("| Visit | Level | Cycle | Match s | fps avg | ft avg ms | p95 | p99 | worst | CPU main | GPU | GC KB/frame | heat s | peak units |");
            fpsRows.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            bool fpsPass = true;
            foreach (var v in visits)
            {
                var ss = seconds.Where(s => s.visit == v.index && s.levelT > 5f).ToList();
                var ok = ss.Where(s => !s.heat).ToList();
                if (ss.Count == 0) continue;
                float fps = ok.Count > 0 ? ok.Average(s => s.fps) : -1f, low = ok.Count > 0 ? ok.Count(s => s.fps < 57f) / (float)ok.Count : 0f;
                if (ok.Count > 10 && (fps < 59f || low > 0.05f)) fpsPass = false;
                fpsRows.AppendLine($"| {v.index + 1} | {v.level} | {v.cycle} | {ss.Count} | {fps:0.0} ({low:P0} s < 57) | {Avg(ok, s => s.avg):0.00} | {Avg(ok, s => s.p95):0.00} | {Avg(ok, s => s.p99):0.00} | {(ok.Count > 0 ? ok.Max(s => s.worst) : -1):0.0} | " +
                                   $"{Avg(ok, s => s.cpu):0.00} | {Avg(ok, s => s.gpu):0.00} | {Avg(ok, s => s.gcKb):0.00} | {ss.Count(s => s.heat)} | {v.peakUnits} |");
            }
            float allP95 = Pct(allMatchDt, 0.95f), allP99 = Pct(allMatchDt, 0.99f);
            verdicts.Add(("60 fps in matches (non-heat seconds: avg >= 59, <= 5 % of seconds under 57)", OzQuality.Level == 0 ? V(fpsPass) : "N/A (" + OzQuality.Name + ")", $"all match frames p95 {allP95:0.00} ms, p99 {allP99:0.00} ms"));

            // ---- 2. menu checkpoints vs cycle 1
            var cpRows = new StringBuilder();
            var ends = checkpoints.Where(c => c.label == "end").OrderBy(c => c.cycle).ToList();
            bool leakPass = true; var leakWhy = new List<string>();
            if (ends.Count >= 2)
            {
                var c1 = ends[0];
                cpRows.AppendLine("| Metric | start | " + string.Join(" | ", ends.Select(c => "c" + c.cycle + " end")) + " | growth c" + ends.Last().cycle + " vs c1 | per cycle | verdict |");
                cpRows.AppendLine("|---|---|" + string.Concat(ends.Select(_ => "---|")) + "---|---|---|");
                var start = checkpoints.FirstOrDefault(c => c.cycle == 0);
                foreach (var key in c1.m.Keys)
                {
                    double a = c1.m[key], z = ends.Last().m.TryGetValue(key, out var zz) ? zz : a;
                    int cyc = Math.Max(1, ends.Last().cycle - c1.cycle);
                    double grow = z - a, pct = a > 0 ? grow / a * 100.0 : 0, per = grow / cyc;
                    bool mem = key.EndsWith("_mb", StringComparison.Ordinal);
                    bool highWater = key == "mono_heap_mb" || key == "total_reserved_mb";
                    bool fail;
                    if (highWater) fail = false;
                    else if (mem) fail = a > 0 && pct > LeakPct && grow > LeakMinMb;
                    else { int lim = LeakObjectsPerCycle.TryGetValue(key, out var l) ? l : 50; fail = per > lim || (pct > LeakPct && grow > lim / 5.0); }
                    if (key == "gfx_driver_mb" && a <= 0) fail = false;
                    if (fail) { leakPass = false; leakWhy.Add($"{key} +{grow:0.#} ({pct:0.#} %)"); }
                    cpRows.AppendLine($"| {key} | {(start != null && start.m.TryGetValue(key, out var s0) ? s0.ToString("0.#") : "-")} | " + string.Join(" | ", ends.Select(c => c.m.TryGetValue(key, out var x) ? x.ToString("0.#") : "-")) +
                                      $" | {grow:+0.#;-0.#;0} ({pct:+0.#;-0.#;0} %) | {per:+0.#;-0.#;0} | {(fail ? "**FAIL**" : highWater ? "info (high-water)" : "ok")} |");
                }
                verdicts.Add(("LEAK: menu memory / object counts after each cycle vs cycle 1 (> 5 % or > N objects a cycle)", V(leakPass), leakPass ? "no metric over its bar" : string.Join(", ", leakWhy)));
            }
            else { cpRows.AppendLine("Fewer than 2 complete cycles: no checkpoint comparison."); verdicts.Add(("LEAK: menu checkpoints vs cycle 1", "N/A", "fewer than 2 complete cycles")); }

            // ---- 3. steady-state slope in matches
            var slRows = new StringBuilder();
            slRows.AppendLine("| Visit | Level | Cycle | window s | total allocated MB/min | mono floor MB/min | gfx driver MB/min | units start -> end | verdict |");
            slRows.AppendLine("|---|---|---|---|---|---|---|---|---|");
            bool slopePass = true, slopeWarn = false;
            foreach (var v in visits)
            {
                var ss = seconds.Where(s => s.visit == v.index && s.levelT > 45f).ToList();
                if (ss.Count < 40) { slRows.AppendLine($"| {v.index + 1} | {v.level} | {v.cycle} | {ss.Count} | - | - | - | - | too short |"); continue; }
                float tot = Slope(ss.Select(s => (s.t, s.totalMb)).ToList()), gfx = Slope(ss.Select(s => (s.t, s.gfxMb)).ToList());
                var floor = ss.GroupBy(s => (int)(s.t / 10f)).Select(gp => (gp.First().t, gp.Min(s => s.monoMb))).ToList();
                float mono = Slope(floor);
                string vd = tot > SlopeFailMb ? "FAIL" : tot > SlopeWarnMb || mono > 1f ? "WARN" : "ok";
                if (vd == "FAIL") slopePass = false; if (vd == "WARN") slopeWarn = true;
                slRows.AppendLine($"| {v.index + 1} | {v.level} | {v.cycle} | {ss.Count} | {tot:0.00} | {mono:0.00} | {gfx:0.00} | {ss.First().units} -> {ss.Last().units} | {vd} |");
            }
            verdicts.Add(($"Steady growth in matches (total allocated, after 45 s: WARN > {SlopeWarnMb} MB/min, FAIL > {SlopeFailMb})", V(slopePass, slopeWarn), "units grow during a match, so read WARN against the units column"));

            // ---- 4. GC spikes, 5. hitches
            var playSpikes = spikes.Where(s => s.phase == "match").ToList();
            verdicts.Add(($"GC spikes > {GcSpikeMs} ms in matches", playSpikes.Count == 0 ? "PASS" : "WARN", $"{playSpikes.Count} in matches (worst {(playSpikes.Count > 0 ? playSpikes.Max(s => s.ms) : 0):0.0} ms), {spikes.Count - playSpikes.Count} in loads / menu"));
            var play = hitches.Where(h => h.kind == "play").ToList();
            var bad = play.Where(h => h.dt > HitchFailMs).ToList();
            verdicts.Add(($"Hitches > {HitchFailMs} ms outside loads", V(bad.Count == 0, play.Count > 0), $"{bad.Count} over {HitchFailMs} ms, {play.Count} over {HitchMs} ms in play; {hitches.Count(h => h.kind == "load")} in loads, {hitches.Count(h => h.kind == "soak")} caused by the soak's census"));

            // ---- 6. sky panorama unload
            var fl = visits.Where(v => v.flyover).ToList();
            bool skyPass = fl.All(v => !v.skyLoaded || v.skyUnloaded);
            bool menuSky = true; var skyNames = new List<string>();
            foreach (var lv in levelOrder) { var e = FlybySky.Entry(lv); if (e != null) { if (!string.IsNullOrEmpty(e.textureHi)) skyNames.Add(Path.GetFileName(e.textureHi)); if (!string.IsNullOrEmpty(e.texture)) skyNames.Add(Path.GetFileName(e.texture)); } }
            foreach (var c in checkpoints) foreach (var n in skyNames) if (c.names != null && c.names.Keys.Any(k => k.StartsWith("Texture2D: ", StringComparison.Ordinal) && k.EndsWith(n, StringComparison.Ordinal))) menuSky = false;
            verdicts.Add(("Sky panoramas unloaded after the flyby (in the level and at every menu checkpoint)", fl.Count == 0 ? "N/A (no flyby ran)" : V(skyPass && menuSky), $"{fl.Count(v => v.skyLoaded)} of {fl.Count} flybys loaded the sky, {fl.Count(v => v.skyUnloaded)} unloaded; panorama at a menu checkpoint: {(menuSky ? "never" : "YES")}"));

            // ---- write
            b.AppendLine("## Verdicts");
            b.AppendLine();
            b.AppendLine("| Check | Verdict | Detail |");
            b.AppendLine("|---|---|---|");
            foreach (var (what, v, d) in verdicts) b.AppendLine($"| {what} | **{v}** | {d} |");
            bool overall = verdicts.All(v => v.v != "FAIL") && aborted == null;
            b.AppendLine();
            b.AppendLine($"**OVERALL: {(overall ? "PASS" : "FAIL")}** (FAIL = any FAIL above or an aborted run; WARN rows are for the worker to read, not a fail)");
            b.AppendLine();
            b.AppendLine("## Frame time per level visit (seconds after the first 5 s, heat-capped seconds left out)");
            b.AppendLine(); b.Append(fpsRows); b.AppendLine();
            b.AppendLine("## Menu checkpoints (main menu, after UnloadUnusedAssets + GC.Collect)");
            b.AppendLine(); b.Append(cpRows); b.AppendLine();
            if (ends.Count >= 2)
            {
                var c1 = ends[0]; var cz = ends.Last();
                b.AppendLine("### Top growing object types (last cycle vs cycle 1)");
                b.AppendLine();
                foreach (var kv in cz.types.Select(k => (k.Key, d: k.Value - (c1.types.TryGetValue(k.Key, out var o) ? o : 0))).Where(k => k.d > 0).OrderByDescending(k => k.d).Take(15)) b.AppendLine($"- {kv.Key}: +{kv.d}");
                b.AppendLine();
                b.AppendLine("### Top growing asset names (Material / Texture / Mesh / AudioClip)");
                b.AppendLine();
                foreach (var kv in cz.names.Select(k => (k.Key, d: k.Value - (c1.names.TryGetValue(k.Key, out var o) ? o : 0))).Where(k => k.d > 0).OrderByDescending(k => k.d).Take(15)) b.AppendLine($"- {kv.Key}: +{kv.d}");
                b.AppendLine();
                b.AppendLine("### Static registries / singleton collections that grew (last cycle vs cycle 1, at the menu)");
                b.AppendLine();
                foreach (var kv in cz.regs.Select(k => (k.Key, d: k.Value - (c1.regs.TryGetValue(k.Key, out var o) ? o : 0), now: k.Value)).Where(k => k.d > 0).OrderByDescending(k => k.d).Take(20)) b.AppendLine($"- {kv.Key}: +{kv.d} (now {kv.now})");
                b.AppendLine();
            }
            b.AppendLine("## Main registries at the end of each match (last census of the visit)");
            b.AppendLine();
            b.AppendLine("Largest static / singleton collections in the last census (registries.csv has every census):");
            b.AppendLine();
            foreach (var kv in matchRegs.Where(k => k.Value > 0 && Regex(k.Key)).OrderByDescending(k => k.Value).Take(25)) b.AppendLine($"- {kv.Key}: {kv.Value}");
            b.AppendLine();
            b.AppendLine("## Steady-state growth in matches");
            b.AppendLine(); b.Append(slRows); b.AppendLine();
            b.AppendLine($"## Hitches over {HitchMs} ms in play (load / soak ones are in hitches.csv)");
            b.AppendLine();
            if (play.Count == 0) b.AppendLine("None.");
            foreach (var h in play.OrderByDescending(h => h.dt).Take(40)) b.AppendLine($"- **{h.dt:0} ms** at {h.t - runStart:0.0} s, {h.scene} c{h.cycle} t+{h.levelT:0.0} s{(h.gc ? $", GC collected ({h.gcMs:0.0} ms)" : "")}: {h.ctx}");
            b.AppendLine();
            b.AppendLine($"## GC spikes over {GcSpikeMs} ms (all phases)");
            b.AppendLine();
            if (spikes.Count == 0) b.AppendLine("None.");
            foreach (var s in spikes.OrderByDescending(s => s.ms).Take(25)) b.AppendLine($"- {s.ms:0.0} ms at {s.t - runStart:0.0} s, {s.phase} {s.scene} c{s.cycle}");
            b.AppendLine();
            b.AppendLine("## Flyby sky per level visit");
            b.AppendLine();
            b.AppendLine("| Visit | Level | Cycle | flyover | sky loaded | MB | unloaded after | last unload | winner |");
            b.AppendLine("|---|---|---|---|---|---|---|---|---|");
            foreach (var v in visits) b.AppendLine($"| {v.index + 1} | {v.level} | {v.cycle} | {v.flyover} | {v.skyLoaded} | {(v.skyMb > 0 ? v.skyMb.ToString("0.0") : "-")} | {(v.unloadChecked ? v.skyUnloaded.ToString() : "-")} | {v.lastUnload} | {(v.winner.Length > 0 ? v.winner : "-")} |");
            b.AppendLine();
            b.AppendLine("## Files");
            b.AppendLine();
            b.AppendLine("soak.csv (1 s rows), census.csv, registries.csv, events.csv, hitches.csv, gc_spikes.csv, checkpoint_c*_*.txt, counters.txt (the profiler counters this player exposes), Player.log (if -logFile pointed here).");
            File.WriteAllText(Path.Combine(dir, "SOAK_REPORT.md"), b.ToString());
            Debug.Log($"[OpenZ Soak] report: {Path.Combine(dir, "SOAK_REPORT.md")} OVERALL {(overall ? "PASS" : "FAIL")}");
        }

        static bool Regex(string k)
        {
            var n = k.ToLowerInvariant();
            foreach (var w in new[] { "pool", "decal", "crater", "track", "mark", "corpse", "debris", "wreck", "scorch", "live", "active", "ragdoll", "chunk", "voice", "src", "views", "objects", "fx", "sound", "loop" })
                if (n.Contains(w)) return true;
            return false;
        }

        static float Avg<T>(List<T> l, Func<T, float> f) { var v = l.Select(f).Where(x => x >= 0f).ToList(); return v.Count > 0 ? v.Average() : -1f; }
    }
}
#endif
